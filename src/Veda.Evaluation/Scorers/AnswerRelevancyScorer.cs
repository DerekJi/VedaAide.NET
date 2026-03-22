using Veda.Core;

namespace Veda.Evaluation.Scorers;

/// <summary>
/// 答案相关性评分器：通过问题与回答的 Embedding 余弦相似度，
/// 衡量回答是否切题。返回 [0, 1] 的浮点分数。
/// </summary>
public sealed class AnswerRelevancyScorer(IEmbeddingService embeddingService)
{
    public async Task<float> ScoreAsync(
        string question,
        string answer,
        CancellationToken ct = default)
    {
        var questionEmbedding = await embeddingService.GenerateEmbeddingAsync(question, ct);
        var answerEmbedding   = await embeddingService.GenerateEmbeddingAsync(answer, ct);
        return Math.Clamp(VectorMath.CosineSimilarity(questionEmbedding, answerEmbedding), 0f, 1f);
    }
}
