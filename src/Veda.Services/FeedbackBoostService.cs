namespace Veda.Services;

/// <summary>
/// Chunk boost service based on historical feedback.
/// After the Rerank stage, boosts the ranking of chunks that have positive feedback history.
/// </summary>
public sealed class FeedbackBoostService(IUserMemoryStore userMemoryStore) : IFeedbackBoostService
{
    public async Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> ApplyBoostAsync(
        string userId,
        IReadOnlyList<(DocumentChunk Chunk, float Score)> results,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || results.Count == 0)
            return results;

        var boosted = new List<(DocumentChunk Chunk, float Score)>(results.Count);
        foreach (var (chunk, score) in results)
        {
            var boostFactor = await userMemoryStore.GetBoostFactorAsync(userId, chunk.Id, ct);
            boosted.Add((chunk, score * boostFactor));
        }

        return boosted.OrderByDescending(x => x.Score).ToList();
    }
}
