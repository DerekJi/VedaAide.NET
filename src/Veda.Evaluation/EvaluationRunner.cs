using Veda.Evaluation.Scorers;

namespace Veda.Evaluation;

/// <summary>
/// 评估运行器：从 Golden Dataset 中加载问题 → 调用 RAG 管道 →
/// 为每个答案打三维评分 → 汇总为 <see cref="EvaluationReport"/>。
/// </summary>
public sealed class EvaluationRunner(
    IEvalDatasetRepository datasetRepo,
    IQueryService          queryService,
    FaithfulnessScorer     faithfulnessScorer,
    AnswerRelevancyScorer  relevancyScorer,
    ContextRecallScorer    recallScorer,
    ILogger<EvaluationRunner> logger) : IEvaluationRunner
{
    public async Task<EvaluationReport> RunAsync(
        EvalRunOptions   options,
        CancellationToken ct = default)
    {
        var allQuestions = await datasetRepo.ListAsync(ct);
        var questions = (options.QuestionIds.Length == 0
            ? allQuestions
            : allQuestions.Where(q => options.QuestionIds.Contains(q.Id)).ToList())
            .ToList();

        if (questions.Count == 0)
        {
            logger.LogWarning("EvaluationRunner: no questions found in Golden Dataset");
            return new EvaluationReport();
        }

        logger.LogInformation("EvaluationRunner: starting run for {Count} question(s)", questions.Count);

        var results = new List<EvalResult>(questions.Count);

        foreach (var question in questions)
        {
            ct.ThrowIfCancellationRequested();
            var result = await EvaluateOneAsync(question, options, ct);
            results.Add(result);
            logger.LogDebug(
                "EvaluationRunner: [{Id}] F={F:F2} R={R:F2} C={C:F2}",
                question.Id,
                result.Metrics.Faithfulness,
                result.Metrics.AnswerRelevancy,
                result.Metrics.ContextRecall);
        }

        var report = new EvaluationReport
        {
            ModelName = options.ChatModelOverride ?? string.Empty,
            Results   = results,
        };

        logger.LogInformation(
            "EvaluationRunner: run {RunId} complete — Overall={Overall:F2}",
            report.RunId, report.AvgOverall);

        return report;
    }

    private async Task<EvalResult> EvaluateOneAsync(
        EvalQuestion   question,
        EvalRunOptions options,
        CancellationToken ct)
    {
        var request = new RagQueryRequest { Question = question.Question };
        RagQueryResponse response;

        try
        {
            response = await queryService.QueryAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "EvaluationRunner: RAG query failed for question {Id}", question.Id);
            return new EvalResult
            {
                QuestionId     = question.Id,
                Question       = question.Question,
                ExpectedAnswer = question.ExpectedAnswer,
                ActualAnswer   = string.Empty,
                ModelName      = options.ChatModelOverride ?? string.Empty,
            };
        }

        var context = string.Join("\n\n", response.Sources.Select(s => s.ChunkContent));

        var faithfulness    = await faithfulnessScorer.ScoreAsync(response.Answer, context, ct);
        var answerRelevancy = await relevancyScorer.ScoreAsync(question.Question, response.Answer, ct);
        var contextRecall   = await recallScorer.ScoreAsync(question.ExpectedAnswer, response.Sources, ct);

        return new EvalResult
        {
            QuestionId     = question.Id,
            Question       = question.Question,
            ExpectedAnswer = question.ExpectedAnswer,
            ActualAnswer   = response.Answer,
            Metrics        = new EvalMetrics
            {
                Faithfulness    = faithfulness,
                AnswerRelevancy = answerRelevancy,
                ContextRecall   = contextRecall,
            },
            Sources        = response.Sources,
            IsHallucination = response.IsHallucination,
            ModelName      = options.ChatModelOverride ?? string.Empty,
        };
    }
}
