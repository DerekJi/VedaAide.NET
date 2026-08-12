namespace Veda.Core;

/// <summary>
/// Default threshold constants shared across modules of the RAG pipeline.
/// Can be overridden via the Veda:Rag section of appsettings.json (with RagOptions / IOptions&lt;RagOptions&gt;).
/// </summary>
public static class RagDefaults
{
    /// <summary>
    /// Vector similarity deduplication threshold: during ingestion, a new chunk is considered a near-duplicate and skipped when its similarity to a stored chunk is ≥ this value.
    /// </summary>
    public const float SimilarityDedupThreshold = 0.95f;

    /// <summary>
    /// First-line anti-hallucination threshold: the answer is flagged as a potential hallucination when the highest similarity between the answer embedding and the retrieved content is &lt; this value.
    /// </summary>
    public const float HallucinationSimilarityThreshold = 0.3f;

    /// <summary>
    /// Default minimum similarity threshold at query time: retrieval results below this value are filtered out and do not participate in LLM answer generation.
    /// </summary>
    public const float DefaultMinSimilarity = 0.3f;

    /// <summary>
    /// Reranking candidate count multiplier: TopK × this value candidate chunks from the initial retrieval, then reranked to keep the top TopK.
    /// </summary>
    public const int RerankCandidatesMultiplier = 2;
}
