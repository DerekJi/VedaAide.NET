using Veda.Core.Options;
namespace Veda.Services;

/// <summary>
/// Hybrid retrieval dual-channel fusion implementation.
/// Runs the vector channel and keyword channel concurrently, then fuses them using RRF
/// (Reciprocal Rank Fusion) or a weighted-merge strategy.
/// </summary>
public sealed class HybridRetriever(IVectorStore vectorStore) : IHybridRetriever
{
    /// <summary>Constant k in the RRF formula; the standard value of 60 effectively suppresses head-ranking concentration.</summary>
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

        // The SQLite IVectorStore shares a single Scoped DbContext underneath, and EF Core
        // DbContext does not support concurrent operations. Run the two channels sequentially
        // to stay compatible with both SQLite and CosmosDB.
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
