namespace Veda.Core;

/// <summary>
/// RAG 管道跨模块共享的默认阈值常量。
/// 可通过 appsettings.json 的 Veda:Rag 节覆盖（配合 RagOptions / IOptions&lt;RagOptions&gt;）。
/// </summary>
public static class RagDefaults
{
    /// <summary>
    /// 向量相似度去重阈值：摄取阶段新块与已存储块相似度 ≥ 此值时视为近似重复，跳过存储。
    /// </summary>
    public const float SimilarityDedupThreshold = 0.95f;

    /// <summary>
    /// 防幻觉第一层阈值：回答 Embedding 与检索内容最高相似度 &lt; 此值时标记为潜在幻觉。
    /// </summary>
    public const float HallucinationSimilarityThreshold = 0.3f;

    /// <summary>
    /// 查询阶段默认最低相似度阈值：低于此值的检索结果被过滤，不参与 LLM 回答生成。
    /// </summary>
    public const float DefaultMinSimilarity = 0.3f;

    /// <summary>
    /// Reranking 候选数量倍数：初始检索 TopK × 此值 个候选块，再重排取前 TopK 个。
    /// </summary>
    public const int RerankCandidatesMultiplier = 2;
}
