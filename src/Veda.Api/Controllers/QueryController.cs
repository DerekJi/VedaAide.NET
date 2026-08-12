using Microsoft.AspNetCore.Authorization;
using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class QueryController(IQueryService queryService) : ControllerBase
{
    /// <summary>
    /// Q&A: vector retrieval → LLM generation → returns the answer with sources.
    /// The userId is taken from the JWT token (trusted); a userId in the request body is not accepted to prevent cross-user data access.
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
