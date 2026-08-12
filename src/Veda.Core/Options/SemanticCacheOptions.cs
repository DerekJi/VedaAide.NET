namespace Veda.Core.Options;

/// <summary>
/// Semantic cache configuration (bound to the Veda:SemanticCache configuration section).
/// </summary>
public sealed class SemanticCacheOptions
{
    /// <summary>Whether to enable the semantic cache. Disabled by default.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Semantic similarity threshold: a cache hit occurs when the cosine similarity of the question embedding is above this value.
    /// Range [0, 1], default 0.95.
    /// </summary>
    public float SimilarityThreshold { get; set; } = 0.95f;

    /// <summary>Cache entry time-to-live (seconds). Default 3600 (1 hour).</summary>
    public int TtlSeconds { get; set; } = 3600;
}
