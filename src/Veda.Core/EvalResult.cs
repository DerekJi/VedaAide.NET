namespace Veda.Core;

/// <summary>Evaluation result for a single question, including the actual answer, scoring metrics, and retrieved sources.</summary>
public record EvalResult
{
    public required string QuestionId { get; init; }
    public required string Question { get; init; }
    public required string ExpectedAnswer { get; init; }
    public required string ActualAnswer { get; init; }
    public EvalMetrics Metrics { get; init; } = new();
    public List<SourceReference> Sources { get; init; } = [];
    public bool IsHallucination { get; init; }
    public string ModelName { get; init; } = string.Empty;
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;
}
