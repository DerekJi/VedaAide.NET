namespace Veda.Core;

/// <summary>A standard question-answer pair from the golden dataset, used to evaluate RAG pipeline quality in batch.</summary>
public record EvalQuestion
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string Question { get; init; }
    public required string ExpectedAnswer { get; init; }
    public string[] Tags { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
