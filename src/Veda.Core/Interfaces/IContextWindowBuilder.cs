namespace Veda.Core.Interfaces;

/// <summary>
/// Selects the optimal context set from a list of candidate document chunks within a token budget.
/// </summary>
public interface IContextWindowBuilder
{
    /// <summary>
    /// Selects a set of chunks from the similarity-sorted candidates that does not exceed <paramref name="maxTokens"/> tokens.
    /// </summary>
    IReadOnlyList<DocumentChunk> Build(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        int maxTokens = 3000);
}
