using System.Text.Json.Serialization;

namespace Veda.Storage.Entities;

/// <summary>
/// Vector chunk document model stored in CosmosDB for NoSQL.
/// Property names use camelCase to match CosmosDB JSON conventions.
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

/// <summary>Contains only id + documentId, used to deserialize delete/patch query results.</summary>
internal sealed class CosmosIdOnly
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Partition Key field — Patch/Delete operations must supply the exact PartitionKey.</summary>
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;
}

/// <summary>Lightweight row shape for document listing queries (no embedding/content), used by GetAllDocumentsAsync.</summary>
internal sealed class CosmosDocRow
{
    [JsonPropertyName("documentId")]
    public string DocumentId { get; set; } = string.Empty;

    [JsonPropertyName("documentName")]
    public string DocumentName { get; set; } = string.Empty;

    [JsonPropertyName("documentType")]
    public int DocumentType { get; set; }
}

/// <summary>Lightweight row shape for version-history queries.</summary>
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

/// <summary>Vector search query result containing document fields and the vector distance score.</summary>
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

    /// <summary>Cosine similarity returned by VectorDistance ([-1,1]; higher = more similar)</summary>
    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("supersededAtTicks")]
    public long SupersededAtTicks { get; set; } = 0;
}

/// <summary>Keyword search result containing the BM25 relevance score.</summary>
internal sealed class CosmosKeywordSearchResult
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

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("supersededAtTicks")]
    public long SupersededAtTicks { get; set; } = 0;

    [JsonPropertyName("bm25Score")]
    public double Bm25Score { get; set; }
}
