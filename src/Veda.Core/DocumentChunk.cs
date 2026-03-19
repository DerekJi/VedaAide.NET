namespace Veda.Core;

/// <summary>
/// 文档被分割后的单个文本块，携带向量和元数据。
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
    /// <summary>生成此块向量时使用的 Embedding 模型名称，用于切换模型时的增量重新索引。</summary>
    public string EmbeddingModel { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
