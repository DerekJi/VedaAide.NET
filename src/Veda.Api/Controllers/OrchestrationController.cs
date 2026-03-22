using Microsoft.AspNetCore.Mvc;
using Veda.Agents.Orchestration;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/orchestrate")]
public sealed class OrchestrationController(IOrchestrationService orchestrationService) : ControllerBase
{
    /// <summary>Agent 驱动的问答流程（QueryAgent + EvalAgent）</summary>
    [HttpPost("query")]
    public async Task<IActionResult> Query(
        [FromBody] OrchestrationQueryRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest("Question is required.");

        var result = await orchestrationService.RunQueryFlowAsync(request.Question, ct);
        return Ok(result);
    }

    /// <summary>Agent 驱动的文档摄取流程（DocumentAgent）</summary>
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest(
        [FromBody] OrchestrationIngestRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Content is required.");
        if (string.IsNullOrWhiteSpace(request.DocumentName))
            return BadRequest("DocumentName is required.");

        var result = await orchestrationService.RunIngestFlowAsync(request.Content, request.DocumentName, ct);
        return Ok(result);
    }
}

public record OrchestrationQueryRequest
{
    public string Question { get; init; } = string.Empty;
}

public record OrchestrationIngestRequest
{
    public string Content { get; init; } = string.Empty;
    public string DocumentName { get; init; } = string.Empty;
}
