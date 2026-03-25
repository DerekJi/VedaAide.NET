namespace Veda.Storage.Entities;

/// <summary>
/// SQLite 语义缓存实体。存储问题 embedding（BLOB）与对应答案，用于避免重复调用 LLM。
/// </summary>
public class SemanticCacheEntity
{
    public string Id              { get; set; } = string.Empty;
    public byte[] EmbeddingBlob   { get; set; } = Array.Empty<byte>();
    public string Answer          { get; set; } = string.Empty;
    public long   CreatedAtTicks  { get; set; }
    public long   ExpiresAtTicks  { get; set; }
}
