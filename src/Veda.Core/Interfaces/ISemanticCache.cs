namespace Veda.Core.Interfaces;

/// <summary>
/// Semantic cache interface: caches generated answers based on question embedding similarity, avoiding repeated LLM calls for semantically identical questions.
/// </summary>
public interface ISemanticCache
{
    /// <summary>
    /// Finds a cached answer whose semantic similarity to <paramref name="questionEmbedding"/> exceeds the threshold.
    /// Returns null on a cache miss.
    /// </summary>
    Task<string?> GetAsync(float[] questionEmbedding, CancellationToken ct = default);

    /// <summary>
    /// Writes the question embedding and its corresponding answer to the cache.
    /// </summary>
    Task SetAsync(float[] questionEmbedding, string answer, CancellationToken ct = default);

    /// <summary>
    /// Clears all cache entries.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the number of currently valid (non-expired) cache entries.
    /// </summary>
    Task<int> GetCountAsync(CancellationToken ct = default);
}
