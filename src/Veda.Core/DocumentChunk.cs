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
    /// <summary>知识作用域元数据，用于多维度过滤；null 表示无限制范围。</summary>
    public KnowledgeScope? Scope { get; init; }
    /// <summary>文档版本号，首次摄取为 1，每次内容变更递增。</summary>
    public int Version { get; set; } = 1;
    /// <summary>本 chunk 被新版本取代的时间；null 表示当前有效。</summary>
    public DateTimeOffset? SupersededAt { get; init; }
    /// <summary>取代本 chunk 的新 chunk ID；null 表示当前有效。</summary>
    public string? SupersededBy { get; init; }
}
