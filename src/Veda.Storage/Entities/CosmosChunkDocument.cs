using System.Text.Json.Serialization;

namespace Veda.Storage.Entities;

/// <summary>
/// CosmosDB for NoSQL 中存储的向量块文档模型。
/// 属性名称使用小驼峰以匹配 CosmosDB JSON 惯例。
/// </summary>
internal sealed class CosmosChunkDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentName")]
    public string DocumentName { get; set; } = string.Empty;

    [JsonPropertyName("documentType")]
    public int DocumentType { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];

    [JsonPropertyName("embeddingModel")]
    public string EmbeddingModel { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("createdAtTicks")]
    public long CreatedAtTicks { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("supersededAtTicks")]
    public long SupersededAtTicks { get; set; } = 0;

    [JsonPropertyName("supersededByDocId")]
    public string SupersededByDocId { get; set; } = string.Empty;
}

/// <summary>仅包含 id + documentId 字段，用于删除/Patch 查询结果反序列化。</summary>
internal sealed class CosmosIdOnly
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Partition Key 字段——Patch/Delete 操作必须提供精确的 PartitionKey。</summary>
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;
}

/// <summary>文档列表查询用的轻量行结构（无 embedding/content），用于 GetAllDocumentsAsync。</summary>
internal sealed class CosmosDocRow
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentName")]
    public string DocumentName { get; set; } = string.Empty;

    [JsonPropertyName("documentType")]
    public int DocumentType { get; set; }
}

/// <summary>版本历史查询用的轻量行结构。</summary>
internal sealed class CosmosVersionRow
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("createdAtTicks")]
    public long CreatedAtTicks { get; set; }

    [JsonPropertyName("supersededAtTicks")]
    public long SupersededAtTicks { get; set; }
}

/// <summary>向量检索查询结果，包含文档字段和向量距离分数。</summary>
internal sealed class CosmosSearchResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentName")]
    public string DocumentName { get; set; } = string.Empty;

    [JsonPropertyName("documentType")]
    public int DocumentType { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("chunkIndex")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    [JsonPropertyName("embeddingModel")]
    public string EmbeddingModel { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    [JsonPropertyName("createdAtTicks")]
    public long CreatedAtTicks { get; set; }

    /// <summary>VectorDistance 返回的余弦距离（越小越相似，0 = 完全相同）</summary>
    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("supersededAtTicks")]
    public long SupersededAtTicks { get; set; } = 0;
}
