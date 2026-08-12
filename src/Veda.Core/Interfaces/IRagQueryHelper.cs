namespace Veda.Core.Interfaces;

/// <summary>
/// Shared helper service interface for RAG queries: provides common logic such as retrieval, ranking, and context building.
/// </summary>
public interface IRagQueryHelper
{
    /// <summary>Retrieves candidates: selects hybrid or vector retrieval based on configuration.</summary>
    Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> RetrieveCandidatesAsync(
        string expandedQuestion,
        float[] queryEmbedding,
        RagQueryRequest request,
        CancellationToken ct);

    /// <summary>Ranking and feedback boost: applies the user-feedback boost after a lightweight rerank.</summary>
    Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> RerankAndBoostAsync(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        string question,
        int topK,
        string? userId,
        CancellationToken ct);

    /// <summary>Lightweight rerank: 70% vector similarity + 30% question keyword coverage.</summary>
    IReadOnlyList<(DocumentChunk Chunk, float Similarity)> Rerank(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        string question,
        int topK);

    /// <summary>Builds context: builds context from a list of text chunks trimmed to the token budget.</summary>
    string BuildContext(IReadOnlyList<DocumentChunk> chunks, string? ephemeralContext = null);

    /// <summary>Detects whether the answer is a hallucination.</summary>
    Task<bool> DetectHallucinationAsync(
        string answer,
        string context,
        RagQueryRequest request,
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> results,
        CancellationToken ct);
}
