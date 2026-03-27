using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QueryController(IQueryService queryService) : ControllerBase
{
    /// <summary>
    /// 问答：向量检索 → LLM 生成 → 返回答案及来源。
    /// userId 优先从 JWT Token 提取；未登录时退回请求体中的值（零售兼容期）。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RagQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Query(
        [FromBody] QueryRequest request,
        [FromServices] ICurrentUserService currentUser,
        CancellationToken ct)
    {
        // JWT 认证后 userId 来自 Token（可信）；未登录时 fallback 到请求体（兼容旧前端）
        var userId = currentUser.UserId ?? request.UserId;

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
            Scope = userId is not null
                ? new KnowledgeScope(OwnerId: userId)
                : (request.ScopeDomain is not null || request.ScopeOwnerId is not null)
                    ? new KnowledgeScope(Domain: request.ScopeDomain, OwnerId: request.ScopeOwnerId)
                    : null,
            UserId = userId
        };

        var response = await queryService.QueryAsync(ragRequest, ct);
        return Ok(response);
    }
}
