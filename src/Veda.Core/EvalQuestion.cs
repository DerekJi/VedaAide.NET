namespace Veda.Core;

/// <summary>Golden dataset 中的一个标准问答对，用于批量评估 RAG 管道质量。</summary>
public record EvalQuestion
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string Question { get; init; }
    public required string ExpectedAnswer { get; init; }
    public string[] Tags { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
