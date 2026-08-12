namespace Veda.Core;

/// <summary>
/// A single text block produced by splitting a document, carrying its vector and metadata.
/// </summary>
public class DocumentChunk
{
    public string Id           { get; init; } = Guid.NewGuid().ToString();
    public string DocumentId   { get; init; } = string.Empty;
    public string DocumentName { get; init; } = string.Empty;
    public DocumentType DocumentType { get; init; }
    public string Content      { get; init; } = string.Empty;
    public int ChunkIndex      { get; init; }
    public float[]? Embedding  { get; set; }
    /// <summary>Name of the Embedding model used to generate this chunk's vector, for incremental re-indexing when switching models.</summary>
    public string EmbeddingModel { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Knowledge-scope metadata used for multi-dimensional filtering; null means unrestricted scope.</summary>
    public KnowledgeScope? Scope { get; init; }
    /// <summary>Document version number, starting at 1 on first ingestion and incremented on every content change.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Time when this chunk was superseded by a newer version; null means it is currently valid.</summary>
    public DateTimeOffset? SupersededAt { get; init; }
    /// <summary>ID of the new chunk that supersedes this one; null means it is currently valid.</summary>
    public string? SupersededBy { get; init; }
}
