namespace Veda.Core.Interfaces;

/// <summary>
/// 基于历史反馈的 chunk boost 服务接口。
/// 在 Rerank 后对有正向反馈历史的 chunk 提升排名权重。
/// </summary>
public interface IFeedbackBoostService
{
    /// <summary>
    /// 对 Rerank 结果列表施加用户反馈 boost，返回重新排序的结果。
    /// 无历史时各 chunk boost = 1.0，排序不变。
    /// </summary>
    Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> ApplyBoostAsync(
        string userId,
        IReadOnlyList<(DocumentChunk Chunk, float Score)> results,
        CancellationToken ct = default);
}
