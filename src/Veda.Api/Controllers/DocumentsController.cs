using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController(
    IDocumentIngestor ingestor,
    IVectorStore      vectorStore,
    ILogger<DocumentsController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp",
        "image/tiff", "image/bmp", "application/pdf"
    };

    /// <summary>列出已 Ingest 的所有文档摘要（文档级汇总，含 chunk 数量）。</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocuments(CancellationToken ct)
    {
        var docs = await vectorStore.GetAllDocumentsAsync(ct);
        return Ok(docs);
    }

    /// <summary>获取指定文档的所有当前 chunk 内容（用于答案来源校验）。</summary>
    [HttpGet("{documentName}/chunks")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChunks(string documentName, CancellationToken ct)
    {
        var chunks = await vectorStore.GetCurrentChunksByDocumentNameAsync(documentName, ct);
        if (chunks.Count == 0) return NotFound(new { error = $"No chunks found for '{documentName}'." });

        return Ok(chunks.Select(c => new
        {
            c.ChunkIndex,
            c.Content,
            documentType = c.DocumentType.ToString()
        }));
    }

    /// <summary>摄取纯文本文档：分块 → Embedding → 去重 → 存储。</summary>
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

    /// <summary>
    /// 摄取图片或 PDF 文件（multipart/form-data）：文件提取 → 分块 → Embedding → 去重 → 存储。
    /// 路由策略：documentType=RichMedia → Vision 模型；其余 → Azure AI Document Intelligence。
    /// </summary>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromQuery] string? documentType = null,
        [FromQuery] string? documentName = null,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file uploaded.");

        if (!AllowedMimeTypes.Contains(file.ContentType))
            return BadRequest(
                $"Unsupported file type '{file.ContentType}'. " +
                "Allowed: JPEG, PNG, WebP, TIFF, BMP, PDF.");

        var name    = string.IsNullOrWhiteSpace(documentName)
            ? System.IO.Path.GetFileName(file.FileName)
            : documentName;
        var docType = DocumentTypeParser.ParseOrDefault(
            documentType, DocumentTypeParser.InferFromName(name));

        await using var stream = file.OpenReadStream();
        var result = await ingestor.IngestFileAsync(stream, name, file.ContentType, docType, ct);

        logger.LogInformation(
            "File upload ingested {Count} chunks from '{Name}' (type={Type})",
            result.ChunksStored, result.DocumentName, docType);

        return StatusCode(StatusCodes.Status201Created, result);
    }
}
