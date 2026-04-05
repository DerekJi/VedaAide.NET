using Veda.Core.Options;
namespace Veda.Core.Interfaces;

/// <summary>
/// 混合检索双通道融合服务。
/// 并发执行向量通道与关键词通道，通过 RRF 或加权合并策略返回融合排序结果。
/// </summary>
public interface IHybridRetriever
{
    Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> RetrieveAsync(
        string query,
        float[] queryEmbedding,
        int topK,
        HybridRetrievalOptions options,
        KnowledgeScope? scope = null,
        float minSimilarity = 0f,
        DocumentType? filterType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        CancellationToken ct = default);
}
