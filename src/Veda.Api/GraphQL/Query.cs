using Veda.Api.Models;

namespace Veda.Api.GraphQL;

/// <summary>
/// HotChocolate GraphQL Query 类型。
/// 并行 REST，提供更灵活的字段选择能力。
/// </summary>
public sealed class Query
{
    /// <summary>
    /// 问答查询（非流式）：检索 + LLM 生成 + 防幻觉校验。
    /// </summary>
    public async Task<RagQueryResponse> AskAsync(
        string question,
        [Service] IQueryService queryService,
        string? documentType = null,
        int topK = 5,
        float minSimilarity = 0.6f,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        CancellationToken ct = default)
    {
        var request = new RagQueryRequest
        {
            Question = question,
            FilterDocumentType = DocumentTypeParser.ParseOrNull(documentType),
            TopK = topK,
            MinSimilarity = minSimilarity,
            DateFrom = dateFrom,
            DateTo = dateTo
        };
        return await queryService.QueryAsync(request, ct);
    }
}
