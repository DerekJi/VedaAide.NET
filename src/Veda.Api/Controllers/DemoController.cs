using Microsoft.AspNetCore.Authorization;
using Veda.Core.Interfaces;

namespace Veda.Api.Controllers;

/// <summary>
/// Demo document library endpoints.
/// Lists the preset sample documents under the Blob Storage demo-documents/ prefix and supports one-click ingestion.
/// Recruiters can experience RAG Q&A directly without uploading anything.
/// </summary>
[ApiController]
[Route("api/demo")]
[Authorize]
public sealed class DemoController(
    IDemoLibraryService        demoLibrary,
    ICurrentUserService        currentUser,
    ILogger<DemoController>    logger) : ControllerBase
{
    /// <summary>Lists the available sample documents.</summary>
    [HttpGet("documents")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDemoDocuments(CancellationToken ct)
    {
        var docs = await demoLibrary.ListAsync(ct);
        return Ok(docs);
    }

    /// <summary>Ingests the specified sample document into the current user's knowledge base (carrying the OwnerId scope).</summary>
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
