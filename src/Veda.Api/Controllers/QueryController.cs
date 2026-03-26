using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QueryController(IQueryService queryService) : ControllerBase
{
    /// <summary>
    /// 问答：向量检索 → LLM 生成 → 返回答案及来源。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RagQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Query([FromBody] QueryRequest request, CancellationToken ct)
    {
        var ragRequest = new RagQueryRequest
        {
            Question = request.Question,
            FilterDocumentType = DocumentTypeParser.ParseOrNull(request.DocumentType),
            TopK = request.TopK,
            MinSimilarity = request.MinSimilarity,
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            Mode = request.Mode,
            StructuredOutput = request.StructuredOutput,
            Scope = (request.ScopeDomain is not null || request.ScopeOwnerId is not null)
                ? new KnowledgeScope(Domain: request.ScopeDomain, OwnerId: request.ScopeOwnerId)
                : null,
            UserId = request.UserId
        };

        var response = await queryService.QueryAsync(ragRequest, ct);
        return Ok(response);
    }
}
