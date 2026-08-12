using Veda.Core;

namespace Veda.Evaluation.Scorers;

/// <summary>
/// Context recall scorer: compares the expected answer against the retrieved document chunks via Embedding
/// similarity to determine whether the retrieval results cover the information needed to answer. Returns a float score in [0, 1].
/// </summary>
public sealed class ContextRecallScorer(IEmbeddingService embeddingService)
{
    public async Task<float> ScoreAsync(
        string expectedAnswer,
        IReadOnlyList<SourceReference> sources,
        CancellationToken ct = default)
    {
        if (sources.Count == 0)
            return 0f;

        var expectedEmbedding = await embeddingService.GenerateEmbeddingAsync(expectedAnswer, ct);

        var sourceEmbeddings = await embeddingService.GenerateEmbeddingsAsync(
            sources.Select(s => s.ChunkContent), ct);

        var maxSimilarity = sourceEmbeddings
            .Select(e => VectorMath.CosineSimilarity(expectedEmbedding, e))
            .DefaultIfEmpty(0f)
            .Max();

        return Math.Clamp(maxSimilarity, 0f, 1f);
    }
}
