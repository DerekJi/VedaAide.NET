namespace Veda.Core;

/// <summary>混合检索融合策略。</summary>
public enum FusionStrategy
{
    /// <summary>Reciprocal Rank Fusion：score = Σ 1/(k+rank)，k=60。</summary>
    Rrf,
    /// <summary>加权合并：VectorWeight × vectorScore + KeywordWeight × keywordScore。</summary>
    WeightedSum
}

/// <summary>混合检索执行参数。</summary>
public record HybridRetrievalOptions(
    float VectorWeight = 0.7f,
    float KeywordWeight = 0.3f,
    FusionStrategy Strategy = FusionStrategy.Rrf);
