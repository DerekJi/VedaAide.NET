using Microsoft.AspNetCore.Authorization;
using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class QueryController(IQueryService queryService) : ControllerBase
{
    /// <summary>
    /// 问答：向量检索 → LLM 生成 → 返回答案及来源。
    /// userId 从 JWT Token 提取（可信），不接受请求体中的 userId 以防跨用户数据访问。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RagQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Query(
        [FromBody] QueryRequest request,
        [FromServices] ICurrentUserService currentUser,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;

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
                : null,
            UserId = userId
        };

        var response = await queryService.QueryAsync(ragRequest, ct);
        return Ok(response);
    }
}
