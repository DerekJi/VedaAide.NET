namespace Veda.Core.Interfaces;

/// <summary>
/// Read/write contract for the vector store. Phase 1 uses a SQLite implementation, which can later be replaced with Azure AI Search or similar.
/// </summary>
public interface IVectorStore
{
    Task UpsertAsync(DocumentChunk chunk, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default);

    /// <summary>Vector semantic search channel, supporting KnowledgeScope filtering.</summary>
    Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        float minSimilarity = 0.6f,
        DocumentType? filterType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        KnowledgeScope? scope = null,
        CancellationToken ct = default);

    /// <summary>
    /// Keyword search channel (a BM25 substitute).
    /// CosmosDB uses CONTAINS full-text matching, while SQLite uses in-memory LIKE filtering.
    /// </summary>
    Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> SearchByKeywordsAsync(
        string query,
        int topK = 5,
        DocumentType? filterType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        KnowledgeScope? scope = null,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(string contentHash, CancellationToken ct = default);
    Task DeleteByDocumentAsync(string documentId, CancellationToken ct = default);

    /// <summary>Returns all currently valid (non-superseded) chunks for the specified document name.</summary>
    Task<IReadOnlyList<DocumentChunk>> GetCurrentChunksByDocumentNameAsync(
        string documentName, CancellationToken ct = default);

    /// <summary>
    /// Marks all current chunks for the specified document name as superseded (called on version upgrade).
    /// </summary>
    Task MarkDocumentSupersededAsync(
        string documentName, string newDocumentId, CancellationToken ct = default);

    /// <summary>Returns the full version history for the specified document name (including superseded chunks).</summary>
    Task<IReadOnlyList<DocumentVersionInfo>> GetVersionHistoryAsync(
        string documentName, CancellationToken ct = default);

    /// <summary>
    /// Lists summaries of all currently valid documents (without vectors or content), for the MCP list_documents tool.
    /// Sorted by document name, returns deduplicated document-level summaries (chunk count per document).
    /// </summary>
    Task<IReadOnlyList<DocumentSummary>> GetAllDocumentsAsync(
        KnowledgeScope? scope = null,
        CancellationToken ct = default);

    /// <summary>Clears all vector data (admin operation, not restricted by scope). Returns the number of deleted chunks.</summary>
    Task<int> ClearAllAsync(CancellationToken ct = default);
}

/// <summary>Document version history summary (for the history endpoint).</summary>
public record DocumentVersionInfo(
    string DocumentId,
    string DocumentName,
    int Version,
    int ChunkCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SupersededAt);

/// <summary>Document summary (for the list_documents MCP tool), without embeddings or content.</summary>
public record DocumentSummary(
    string DocumentId,
    string DocumentName,
    DocumentType DocumentType,
    int ChunkCount);
