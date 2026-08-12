namespace Veda.Storage.Entities;

/// <summary>
/// EF Core-persisted Prompt template entity.
/// </summary>
public class PromptTemplateEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string Content { get; set; }

    /// <summary>Optional: bound to a specific document type (null = generic template).</summary>
    public int? DocumentType { get; set; }

    public long CreatedAtTicks { get; set; } = DateTimeOffset.UtcNow.UtcTicks;
}
