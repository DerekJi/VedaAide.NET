using Veda.Api.Models;

namespace Veda.Api.GraphQL;

/// <summary>
/// HotChocolate GraphQL Query type.
/// The GraphQL counterpart to REST, offering more flexible field selection.
/// </summary>
public sealed class Query
{
    /// <summary>
    /// Question-answering query (non-streaming): retrieval + LLM generation + anti-hallucination check.
    /// </summary>
    public async Task<RagQueryResponse> AskAsync(
        string question,
        [Service] IQueryService queryService,
        string? documentType = null,
        int topK = 5,
        float minSimilarity = 0.6f,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        bool structuredOutput = false,
        string? scopeDomain = null,
        string? scopeOwnerId = null,
        CancellationToken ct = default)
    {
        var request = new RagQueryRequest
        {
            Question = question,
            FilterDocumentType = DocumentTypeParser.ParseOrNull(documentType),
            TopK = topK,
            MinSimilarity = minSimilarity,
            DateFrom = dateFrom,
            DateTo = dateTo,
            StructuredOutput = structuredOutput,
            Scope = (scopeDomain is not null || scopeOwnerId is not null)
                ? new KnowledgeScope(Domain: scopeDomain, OwnerId: scopeOwnerId)
                : null
        };
        return await queryService.QueryAsync(request, ct);
    }
}
