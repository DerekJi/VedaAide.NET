namespace Veda.Services;

/// <summary>
/// 基于历史反馈的 chunk boost 服务。
/// 在 Rerank 阶段之后，对有正向反馈历史的 chunk 提升排名。
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
