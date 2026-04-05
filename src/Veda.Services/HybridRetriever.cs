using Veda.Core.Options;
namespace Veda.Services;

/// <summary>
/// 混合检索双通道融合实现。
/// 并发执行向量通道与关键词通道，使用 RRF（Reciprocal Rank Fusion）或加权合并策略。
/// </summary>
public sealed class HybridRetriever(IVectorStore vectorStore) : IHybridRetriever
{
    /// <summary>RRF 公式中的常数 k，标准值 60 可有效抑制头部集中效应。</summary>
    private const int RrfK = 60;

    public async Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> RetrieveAsync(
        string query,
        float[] queryEmbedding,
        int topK,
        HybridRetrievalOptions options,
        KnowledgeScope? scope = null,
        float minSimilarity = 0f,
        DocumentType? filterType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        CancellationToken ct = default)
    {
        var candidateK = topK * 4;

        // SQLite 实现的 IVectorStore 底层共享同一个 DbContext（Scoped），
        // EF Core DbContext 不支持并发操作。顺序执行两个通道，确保兼容 SQLite 和 CosmosDB。
        var vectorResults = await vectorStore.SearchAsync(
            queryEmbedding, topK: candidateK, minSimilarity: minSimilarity,
            filterType: filterType, dateFrom: dateFrom, dateTo: dateTo,
            scope: scope, ct: ct);

        var keywordResults = await vectorStore.SearchByKeywordsAsync(
            query, topK: candidateK,
            filterType: filterType, dateFrom: dateFrom, dateTo: dateTo,
            scope: scope, ct: ct);

        return options.Strategy == FusionStrategy.WeightedSum
            ? FuseWeighted(vectorResults, keywordResults, options.VectorWeight, options.KeywordWeight, topK)
            : FuseRrf(vectorResults, keywordResults, topK);
    }

    // ── Fusion Strategies ─────────────────────────────────────────────────────

    private static IReadOnlyList<(DocumentChunk, float)> FuseRrf(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> vectorResults,
        IReadOnlyList<(DocumentChunk Chunk, float Score)> keywordResults,
        int topK)
    {
        var scores = new Dictionary<string, (DocumentChunk Chunk, double Score)>(StringComparer.Ordinal);

        AddRrfScores(scores, vectorResults.Select(x => (x.Chunk, x.Similarity)));
        AddRrfScores(scores, keywordResults);

        return scores.Values
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => (x.Chunk, (float)x.Score))
            .ToList()
            .AsReadOnly();
    }

    private static void AddRrfScores(
        Dictionary<string, (DocumentChunk Chunk, double Score)> scores,
        IEnumerable<(DocumentChunk Chunk, float Score)> ranked)
    {
        var rank = 1;
        foreach (var (chunk, _) in ranked)
        {
            var rrfScore = 1.0 / (RrfK + rank++);
            if (scores.TryGetValue(chunk.Id, out var existing))
                scores[chunk.Id] = (existing.Chunk, existing.Score + rrfScore);
            else
                scores[chunk.Id] = (chunk, rrfScore);
        }
    }

    private static IReadOnlyList<(DocumentChunk, float)> FuseWeighted(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> vectorResults,
        IReadOnlyList<(DocumentChunk Chunk, float Score)> keywordResults,
        float vectorWeight,
        float keywordWeight,
        int topK)
    {
        var scores = new Dictionary<string, (DocumentChunk Chunk, double Score)>(StringComparer.Ordinal);

        foreach (var (chunk, sim) in vectorResults)
        {
            if (scores.TryGetValue(chunk.Id, out var existing))
                scores[chunk.Id] = (existing.Chunk, existing.Score + vectorWeight * sim);
            else
                scores[chunk.Id] = (chunk, vectorWeight * (double)sim);
        }

        foreach (var (chunk, score) in keywordResults)
        {
            if (scores.TryGetValue(chunk.Id, out var existing))
                scores[chunk.Id] = (existing.Chunk, existing.Score + keywordWeight * score);
            else
                scores[chunk.Id] = (chunk, keywordWeight * (double)score);
        }

        return scores.Values
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => (x.Chunk, (float)x.Score))
            .ToList()
            .AsReadOnly();
    }
}
