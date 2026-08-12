namespace Veda.Core.Options;

/// <summary>Fusion strategy for hybrid retrieval.</summary>
public enum FusionStrategy
{
    /// <summary>Reciprocal Rank Fusion: score = Σ 1/(k+rank), k = 60.</summary>
    Rrf,
    /// <summary>Weighted combination: VectorWeight × vectorScore + KeywordWeight × keywordScore.</summary>
    WeightedSum
}

/// <summary>Execution parameters for hybrid retrieval.</summary>
public record HybridRetrievalOptions(
    float VectorWeight = 0.7f,
    float KeywordWeight = 0.3f,
    FusionStrategy Strategy = FusionStrategy.Rrf);
