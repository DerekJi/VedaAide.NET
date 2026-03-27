namespace Veda.Core.Interfaces;

/// <summary>
/// 语义缓存接口：基于问题 embedding 相似度缓存已生成的答案，避免对相同语义问题重复调用 LLM。
/// </summary>
public interface ISemanticCache
{
    /// <summary>
    /// 查找与 <paramref name="questionEmbedding"/> 语义相似度超过阈值的缓存答案。
    /// 未命中时返回 null。
    /// </summary>
    Task<string?> GetAsync(float[] questionEmbedding, CancellationToken ct = default);

    /// <summary>
    /// 将问题 embedding 与对应答案写入缓存。
    /// </summary>
    Task SetAsync(float[] questionEmbedding, string answer, CancellationToken ct = default);

    /// <summary>
    /// 清空全部缓存条目。
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// 返回当前有效（未过期）的缓存条目数量。
    /// </summary>
    Task<int> GetCountAsync(CancellationToken ct = default);
}
