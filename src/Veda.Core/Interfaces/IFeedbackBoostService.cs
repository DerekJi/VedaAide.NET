namespace Veda.Core.Interfaces;

/// <summary>
/// Service interface for boosting chunks based on historical feedback.
/// After reranking, raises the ranking weight of chunks with a history of positive feedback.
/// </summary>
public interface IFeedbackBoostService
{
    /// <summary>
    /// Applies the user-feedback boost to a reranked result list and returns the reordered results.
    /// With no feedback history, every chunk keeps boost = 1.0 and the order is unchanged.
    /// </summary>
    Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> ApplyBoostAsync(
        string userId,
        IReadOnlyList<(DocumentChunk Chunk, float Score)> results,
        CancellationToken ct = default);
}
