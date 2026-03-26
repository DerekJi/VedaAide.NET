namespace Veda.Core.Interfaces;

/// <summary>
/// 用户级私有记忆层接口。
/// 记录行为事件，提供基于历史反馈的检索权重偏好。
/// </summary>
public interface IUserMemoryStore
{
    /// <summary>记录行为事件（异步，不阻塞主流程）。</summary>
    Task RecordEventAsync(UserBehaviorEvent evt, CancellationToken ct = default);

    /// <summary>
    /// 获取用户对特定 chunk 的权重 boost 因子。
    /// 无历史时返回 1.0（不影响排序）；正向反馈后 > 1.0，负向反馈后 < 1.0。
    /// </summary>
    Task<float> GetBoostFactorAsync(string userId, string chunkId, CancellationToken ct = default);

    /// <summary>获取用户的个性化术语偏好（正向反馈中频繁出现的词汇）。</summary>
    Task<IReadOnlyDictionary<string, float>> GetTermPreferencesAsync(
        string userId, CancellationToken ct = default);

    /// <summary>返回反馈统计数据（用于 admin stats 端点）。</summary>
    Task<FeedbackStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>反馈统计汇总。</summary>
public record FeedbackStats(
    int TotalEvents,
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<RejectedChunkInfo> TopRejectedChunks);

/// <summary>高频被标记无关的 chunk 信息。</summary>
public record RejectedChunkInfo(
    string ChunkId,
    string? DocumentName,
    int RejectionCount);
