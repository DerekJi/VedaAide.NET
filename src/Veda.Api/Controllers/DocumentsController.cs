using Microsoft.AspNetCore.Authorization;
using Veda.Api.Models;

namespace Veda.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentsController(
    IDocumentIngestor ingestor,
    IVectorStore      vectorStore,
    ICurrentUserService currentUser,
    ILogger<DocumentsController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp",
        "image/tiff", "image/bmp", "application/pdf"
    };

    /// <summary>Lists the documents the current user has ingested (isolated by OwnerId).</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDocuments(CancellationToken ct)
    {
        var scope = currentUser.UserId is not null
            ? new KnowledgeScope(OwnerId: currentUser.UserId)
            : null;
        var docs = await vectorStore.GetAllDocumentsAsync(scope: scope, ct: ct);
        return Ok(docs);
    }

    /// <summary>Gets all current chunk content of the specified document (for answer source verification).</summary>
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

    /// <summary>Ingests a plain-text document: chunking → Embedding → dedup → storage.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(IngestResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        var docType = DocumentTypeParser.ParseOrDefault(request.DocumentType);
        var scope   = currentUser.UserId is not null
            ? new KnowledgeScope(OwnerId: currentUser.UserId)
            : null;
        var result = await ingestor.IngestAsync(request.Content, request.DocumentName, docType, scope, ct);
        logger.LogInformation("Ingested {Count} chunks from '{Name}'", result.ChunksStored, result.DocumentName);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Ingests image or PDF files (multipart/form-data): file extraction → chunking → Embedding → dedup → storage.
    /// Routing policy: documentType=RichMedia → Vision model; everything else → Azure AI Document Intelligence.
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
        var scope   = currentUser.UserId is not null
            ? new KnowledgeScope(OwnerId: currentUser.UserId)
            : null;

        await using var stream = file.OpenReadStream();
        var result = await ingestor.IngestFileAsync(stream, name, file.ContentType, docType, scope, ct);

        logger.LogInformation(
            "File upload ingested {Count} chunks from '{Name}' (type={Type})",
            result.ChunksStored, result.DocumentName, docType);

        return StatusCode(StatusCodes.Status201Created, result);
    }
}
