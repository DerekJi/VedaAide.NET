namespace Veda.Core.Options;

/// <summary>
/// Configurable thresholds for the RAG pipeline; the Veda:Rag section in appsettings.json overrides the defaults.
/// </summary>
public sealed class RagOptions
{
    /// <inheritdoc cref="RagDefaults.SimilarityDedupThreshold"/>
    public float SimilarityDedupThreshold { get; set; } = RagDefaults.SimilarityDedupThreshold;

    /// <inheritdoc cref="RagDefaults.HallucinationSimilarityThreshold"/>
    public float HallucinationSimilarityThreshold { get; set; } = RagDefaults.HallucinationSimilarityThreshold;

    /// <summary>
    /// Default minimum similarity threshold for the query stage. Used when the client does not pass minSimilarity.
    /// </summary>
    public float DefaultMinSimilarity { get; set; } = RagDefaults.DefaultMinSimilarity;

    /// <summary>
    /// Whether to enable the second layer of hallucination prevention (LLM self-check).
    /// When enabled, each query consumes one extra LLM call; off by default.
    /// </summary>
    public bool EnableSelfCheckGuard { get; set; } = false;

    /// <summary>Whether to enable hybrid retrieval with both channels (vector + keyword RRF fusion). Off by default.</summary>
    public bool HybridRetrievalEnabled { get; set; } = false;

    /// <summary>Weight of the vector channel in hybrid retrieval (only effective with the WeightedSum strategy).</summary>
    public float VectorWeight { get; set; } = 0.7f;

    /// <summary>Weight of the keyword channel in hybrid retrieval (only effective with the WeightedSum strategy).</summary>
    public float KeywordWeight { get; set; } = 0.3f;

    /// <summary>Hybrid retrieval fusion strategy: Rrf (default) or WeightedSum.</summary>
    public FusionStrategy FusionStrategy { get; set; } = FusionStrategy.Rrf;
}
