namespace Veda.Core;

/// <summary>
/// 语义缓存配置项（绑定到 Veda:SemanticCache 配置节）。
/// </summary>
public sealed class SemanticCacheOptions
{
    /// <summary>是否启用语义缓存。默认禁用。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 语义相似度阈值：问题 embedding 余弦相似度高于此值时命中缓存。
    /// 值域 [0, 1]，默认 0.95。
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.95f;

    /// <summary>缓存条目存活时间（秒）。默认 3600（1 小时）。</summary>
    public int TtlSeconds { get; set; } = 3600;
}
