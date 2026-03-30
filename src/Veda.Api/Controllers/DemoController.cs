namespace Veda.Api.Controllers;

/// <summary>
/// 演示文档库端点。
/// 列出 Blob Storage demo-documents/ 前缀的预置示例文档，支持一键 ingest。
/// 招聘方可零上传直接体验 RAG 问答效果。
/// </summary>
[ApiController]
[Route("api/demo")]
public sealed class DemoController(
    IDemoLibraryService        demoLibrary,
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

    /// <summary>将指定示例文档 ingest 到公共知识库。</summary>
    [HttpPost("documents/{name}/ingest")]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IngestDemoDocument(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Document name is required." });

        try
        {
            var result = await demoLibrary.IngestAsync(name, ct);
            logger.LogInformation("Demo ingest: '{Name}' → {Count} chunks", name, result.ChunksStored);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Demo ingest blocked: {Msg}", ex.Message);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }
}
