namespace Veda.Storage.Entities;

/// <summary>
/// SQLite semantic cache entity. Stores question embeddings (BLOB) and corresponding answers to avoid repeated LLM calls.
/// </summary>
public class SemanticCacheEntity
{
    public string Id              { get; set; } = string.Empty;
    public byte[] EmbeddingBlob   { get; set; } = Array.Empty<byte>();
    public string Answer          { get; set; } = string.Empty;
    public long   CreatedAtTicks  { get; set; }
    public long   ExpiresAtTicks  { get; set; }
}
