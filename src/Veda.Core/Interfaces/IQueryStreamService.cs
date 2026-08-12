namespace Veda.Core.Interfaces;

/// <summary>
/// Streaming question-answering service interface: first yields sources, then yields LLM output token by token, and finally yields done (including the hallucination flag).
/// </summary>
public interface IQueryStreamService
{
    /// <summary>Streaming query: returns a stream of query results in SSE format.</summary>
    IAsyncEnumerable<RagStreamChunk> QueryStreamAsync(
        RagQueryRequest request,
        CancellationToken ct = default);
}
