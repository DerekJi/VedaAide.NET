namespace Veda.Evaluation.Scorers;

/// <summary>
/// 忠实度评分器：通过 LLM 审核回答是否仅基于检索到的上下文，
/// 避免捏造内容。返回 [0, 1] 的浮点分数。
/// </summary>
public sealed class FaithfulnessScorer(IChatService chatService, ILogger<FaithfulnessScorer> logger)
{
    private const string SystemPrompt =
        """
        You are an objective faithfulness evaluator for RAG systems.
        Your task: given a Context and an Answer, rate how faithful the Answer is to the Context.
        Rules:
        - A score of 1.0 means every claim in the Answer can be directly found in or inferred from the Context.
        - A score of 0.0 means the Answer contains claims not supported by the Context at all.
        - Use intermediate values (e.g. 0.3, 0.7) for partially faithful answers.
        - Respond ONLY with a single decimal number from 0.0 to 1.0. No explanation.
        """;

    public async Task<float> ScoreAsync(
        string answer,
        string context,
        CancellationToken ct = default)
    {
        var userMessage =
            $"""
            Context:
            {context}

            Answer to evaluate:
            {answer}
            """;

        try
        {
            var response = await chatService.CompleteAsync(SystemPrompt, userMessage, ct);
            if (float.TryParse(response.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var score))
            {
                return Math.Clamp(score, 0f, 1f);
            }

            logger.LogWarning("FaithfulnessScorer: unexpected LLM response '{Response}', defaulting to 0", response);
            return 0f;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FaithfulnessScorer: LLM call failed");
            return 0f;
        }
    }
}
