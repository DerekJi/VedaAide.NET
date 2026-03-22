namespace Veda.Storage.Entities;

/// <summary>
/// EF Core 持久化的 Prompt 模板实体。
/// </summary>
public class PromptTemplateEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string Content { get; set; }

    /// <summary>可选：绑定特定文档类型（null 表示通用模板）。</summary>
    public int? DocumentType { get; set; }

    public long CreatedAtTicks { get; set; } = DateTimeOffset.UtcNow.UtcTicks;
}
