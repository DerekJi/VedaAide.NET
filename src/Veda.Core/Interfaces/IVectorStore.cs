namespace Veda.Core.Interfaces;

/// <summary>
/// 向量存储的读写契约。Phase 1 使用 SQLite 实现，后续可替换为 Azure AI Search 等。
/// </summary>
public interface IVectorStore
{
    Task UpsertAsync(DocumentChunk chunk, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default);

    /// <summary>向量语义检索通道，支持 KnowledgeScope 过滤。</summary>
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
    /// 关键词检索通道（BM25 平替）。
    /// CosmosDB 使用 CONTAINS 全文匹配，SQLite 使用 LIKE 内存过滤。
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
}
