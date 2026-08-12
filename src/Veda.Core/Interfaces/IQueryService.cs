namespace Veda.Core.Interfaces;

/// <summary>
/// Question-answering query service contract (synchronous query that returns a complete answer).
/// ISP: separated from ingestion operations and from streaming queries.
/// </summary>
public interface IQueryService
{
    Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken ct = default);
}
