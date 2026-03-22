using Veda.Core;

namespace Veda.Evaluation.Scorers;

/// <summary>
/// 上下文召回率评分器：将期望答案与已检索到的文档块做 Embedding 比对，
/// 判断检索结果是否覆盖了回答所需的信息。返回 [0, 1] 的浮点分数。
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
