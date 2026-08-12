namespace Veda.Core;

/// <summary>Complete report of an evaluation run, containing results for all questions and aggregate statistics.</summary>
public record EvaluationReport
{
    public string RunId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset RunAt { get; init; } = DateTimeOffset.UtcNow;
    public string ModelName { get; init; } = string.Empty;
    public List<EvalResult> Results { get; init; } = [];

    public float AvgFaithfulness   => Results.Count == 0 ? 0f : Results.Average(r => r.Metrics.Faithfulness);
    public float AvgAnswerRelevancy => Results.Count == 0 ? 0f : Results.Average(r => r.Metrics.AnswerRelevancy);
    public float AvgContextRecall  => Results.Count == 0 ? 0f : Results.Average(r => r.Metrics.ContextRecall);
    public float AvgOverall        => Results.Count == 0 ? 0f : Results.Average(r => r.Metrics.Overall);
}
