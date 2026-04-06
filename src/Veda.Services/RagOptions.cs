namespace Veda.Core.Options;

/// <summary>
/// RAG 管道可配置阈值，通过 appsettings.json 的 Veda:Rag 节覆盖默认值。
/// </summary>
public sealed class RagOptions
{
    /// <inheritdoc cref="RagDefaults.SimilarityDedupThreshold"/>
    public float SimilarityDedupThreshold { get; set; } = RagDefaults.SimilarityDedupThreshold;

    /// <inheritdoc cref="RagDefaults.HallucinationSimilarityThreshold"/>
    public float HallucinationSimilarityThreshold { get; set; } = RagDefaults.HallucinationSimilarityThreshold;

    /// <summary>
    /// 查询阶段默认最低相似度阈值。客户端未传入 minSimilarity 时使用此值。
    /// </summary>
    public float DefaultMinSimilarity { get; set; } = RagDefaults.DefaultMinSimilarity;

    /// <summary>
    /// 是否启用防幻觉第二层（LLM 自我校验）。
    /// 启用后每次查询额外消耗一次 LLM 调用，默认关闭。
    /// </summary>
    public bool EnableSelfCheckGuard { get; set; } = false;

    /// <summary>是否启用混合检索双通道（向量 + 关键词 RRF 融合）。默认关闭。</summary>
    public bool HybridRetrievalEnabled { get; set; } = false;

    /// <summary>混合检索中向量通道权重（仅 WeightedSum 策略有效）。</summary>
    public float VectorWeight { get; set; } = 0.7f;

    /// <summary>混合检索中关键词通道权重（仅 WeightedSum 策略有效）。</summary>
    public float KeywordWeight { get; set; } = 0.3f;

    /// <summary>混合检索融合策略：Rrf（默认）或 WeightedSum。</summary>
    public FusionStrategy FusionStrategy { get; set; } = FusionStrategy.Rrf;
}
