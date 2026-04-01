namespace Veda.Storage.Entities;

/// <summary>单次 AI 模型调用的 token 消耗持久化实体。</summary>
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
