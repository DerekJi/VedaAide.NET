namespace Veda.Core;

/// <summary>单个问题的评估结果，包含实际回答、评分指标及检索来源。</summary>
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
