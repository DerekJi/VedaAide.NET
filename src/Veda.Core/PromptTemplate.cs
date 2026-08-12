namespace Veda.Core;

/// <summary>
/// Domain model for a prompt template (an immutable record).
/// </summary>
public record PromptTemplate
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Content { get; init; }

    /// <summary>Optional: binds to a specific document type (null means a generic template).</summary>
    public DocumentType? DocumentType { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
