namespace Veda.Storage.Entities;

/// <summary>
/// Vector chunk entity stored in SQLite. The Embedding is serialized as BLOB.
/// </summary>
public class VectorChunkEntity
{
    public string Id           { get; set; } = string.Empty;
    public string DocumentId   { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public int DocumentType    { get; set; }  // DocumentType enum value
    public string Content      { get; set; } = string.Empty;
    public int ChunkIndex      { get; set; }
    public string ContentHash  { get; set; } = string.Empty;  // SHA256 for dedup
    public byte[] EmbeddingBlob { get; set; } = Array.Empty<byte>();  // float[] as little-endian bytes
    public string EmbeddingModel { get; set; } = string.Empty;  // model version used when generating the Embedding; used to re-index when the model is switched
    public string MetadataJson { get; set; } = "{}";
    public long CreatedAtTicks { get; set; }
    /// <summary>Document version, starting at 1 on first ingestion and incremented on every content change.</summary>
    public int Version { get; set; } = 1;
    /// <summary>UTC ticks when this chunk was superseded; 0 means it is currently active.</summary>
    public long SupersededAtTicks { get; set; } = 0;
    /// <summary>New document ID that superseded this chunk (populated on version upgrades).</summary>
    public string SupersededByDocId { get; set; } = string.Empty;
}
