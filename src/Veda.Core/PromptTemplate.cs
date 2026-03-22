namespace Veda.Core;

/// <summary>
/// Prompt 模板领域模型（不可变 record）。
/// </summary>
public record PromptTemplate
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Content { get; init; }

    /// <summary>可选：绑定特定文档类型（null 表示通用模板）。</summary>
    public DocumentType? DocumentType { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
