using Microsoft.AspNetCore.Authorization;
using Veda.Core.Interfaces;

namespace Veda.Api.Controllers;

/// <summary>
/// 演示文档库端点。
/// 列出 Blob Storage demo-documents/ 前缀的预置示例文档，支持一键 ingest。
/// 招聘方可零上传直接体验 RAG 问答效果。
/// </summary>
[ApiController]
[Route("api/demo")]
[Authorize]
public sealed class DemoController(
    IDemoLibraryService        demoLibrary,
    ICurrentUserService        currentUser,
    ILogger<DemoController>    logger) : ControllerBase
{
    /// <summary>列出可用的示例文档。</summary>
    [HttpGet("documents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDemoDocuments(CancellationToken ct)
    {
        var docs = await demoLibrary.ListAsync(ct);
        return Ok(docs);
    }

    /// <summary>将指定示例文档 ingest 到当前用户的知识库（携带 OwnerId scope）。</summary>
    [HttpPost("documents/{name}/ingest")]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IngestDemoDocument(
        string name,
        [FromQuery] string? documentType = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Document name is required." });

        var scope   = currentUser.UserId is not null
            ? new KnowledgeScope(OwnerId: currentUser.UserId)
            : null;
        var docType = DocumentTypeParser.ParseOrNull(documentType);

        try
        {
            var result = await demoLibrary.IngestAsync(name, scope, docType, ct);
            logger.LogInformation("Demo ingest: '{Name}' ({Type}) → {Count} chunks (owner={Owner})",
                name, docType, result.ChunksStored, currentUser.UserId);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Demo ingest blocked: {Msg}", ex.Message);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }
}
