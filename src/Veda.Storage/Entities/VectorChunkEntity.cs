namespace Veda.Storage.Entities;

/// <summary>
/// SQLite 存储的向量块实体。Embedding 序列化为 BLOB。
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
    public string MetadataJson { get; set; } = "{}";
    public long CreatedAtTicks { get; set; }
}
