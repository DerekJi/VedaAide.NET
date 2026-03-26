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
    public string EmbeddingModel { get; set; } = string.Empty;  // 记录生成 Embedding 时使用的模型版本，切换模型时用于重新索引
    public string MetadataJson { get; set; } = "{}";
    public long CreatedAtTicks { get; set; }
    /// <summary>文档版本号，首次摄取为 1，每次内容变更递增。</summary>
    public int Version { get; set; } = 1;
    /// <summary>本 chunk 被取代的 UTC Tick；0 表示当前有效。</summary>
    public long SupersededAtTicks { get; set; } = 0;
    /// <summary>取代本 chunk 的新文档 ID（版本升级时填充）。</summary>
    public string SupersededByDocId { get; set; } = string.Empty;
}
