using Veda.Core;

namespace Veda.Evaluation.Scorers;

/// <summary>
/// Answer relevancy scorer: measures whether an answer stays on topic by computing
/// the Embedding cosine similarity between the question and the answer. Returns a float score in [0, 1].
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
