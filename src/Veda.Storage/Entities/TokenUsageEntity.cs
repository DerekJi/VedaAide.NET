namespace Veda.Storage.Entities;

/// <summary>Persisted entity for the token consumption of a single AI model call.</summary>
public class TokenUsageEntity
{
    public Guid   Id               { get; set; } = Guid.NewGuid();
    public string UserId           { get; set; } = string.Empty;
    public string ModelName        { get; set; } = string.Empty;
    public string OperationType    { get; set; } = string.Empty; // Chat | Embedding | Vision
    public int    PromptTokens     { get; set; }
    public int    CompletionTokens { get; set; }
    public int    TotalTokens      { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
