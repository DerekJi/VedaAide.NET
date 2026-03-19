using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController(IDocumentIngestor ingestor, ILogger<DocumentsController> logger) : ControllerBase
{
    /// <summary>
    /// 摄取文档：分块 → Embedding → 去重 → 存储。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        var docType = DocumentTypeParser.ParseOrDefault(request.DocumentType);
        var result = await ingestor.IngestAsync(request.Content, request.DocumentName, docType, ct);
        logger.LogInformation("Ingested {Count} chunks from '{Name}'", result.ChunksStored, result.DocumentName);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
