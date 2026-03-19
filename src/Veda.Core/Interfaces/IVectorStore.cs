namespace Veda.Core.Interfaces;

/// <summary>
/// 向量存储的读写契约。Phase 1 使用 SQLite 实现，后续可替换为 Azure AI Search 等。
/// </summary>
public interface IVectorStore
{
    Task UpsertAsync(DocumentChunk chunk, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        float minSimilarity = 0.6f,
        DocumentType? filterType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        CancellationToken ct = default);
    Task<bool> ExistsAsync(string contentHash, CancellationToken ct = default);
    Task DeleteByDocumentAsync(string documentId, CancellationToken ct = default);
}
