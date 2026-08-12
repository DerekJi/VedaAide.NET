namespace Veda.Core.Interfaces;

/// <summary>
/// Service contract for converting text into vectors (embeddings).
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default);

    /// <summary>
    /// Expands the query text to enhance semantics.
    /// </summary>
    Task<string> ExpandQueryAsync(string text, CancellationToken ct = default);
}
