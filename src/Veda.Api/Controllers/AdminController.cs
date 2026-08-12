using Microsoft.EntityFrameworkCore;
using Veda.Core.Interfaces;
using Veda.Storage;

namespace Veda.Api.Controllers;

/// <summary>
/// Development admin endpoints, guarded by the Admin API Key (Veda:Security:AdminApiKey).
/// Supports viewing DB status, paging through chunks, clearing data, and deleting specific documents.
/// CosmosDB mode: stats/clear operate through the IVectorStore interface; chunks paging is SQLite-only.
/// </summary>
[ApiController]
[Route("api/admin")]
public sealed class AdminController(
    IVectorStore vectorStore,
    ISemanticCache semanticCache,
    VedaDbContext db,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>Returns vector store statistics (chunk count, document count, cache entry count).</summary>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var chunkCount = await db.VectorChunks.CountAsync(ct);
        var docCount   = await db.VectorChunks
            .Select(c => c.DocumentId)
            .Distinct()
            .CountAsync(ct);
        var syncedFiles = await db.SyncedFiles.CountAsync(ct);
        var cacheCount  = await semanticCache.GetCountAsync(ct);

        return Ok(new
        {
            chunkCount,
            documentCount = docCount,
            syncedFileCount = syncedFiles,
            semanticCacheEntries = cacheCount
        });
    }

    /// <summary>Views all chunks with paging (SQLite mode only).</summary>
    [HttpGet("chunks")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListChunks(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        size = Math.Clamp(size, 1, 100);
        var skip   = (Math.Max(1, page) - 1) * size;
        var total  = await db.VectorChunks.CountAsync(ct);
        var rawChunks = await db.VectorChunks
            .OrderByDescending(c => c.CreatedAtTicks)
            .Skip(skip)
            .Take(size)
            .Select(c => new
            {
                c.Id,
                c.DocumentId,
                c.DocumentName,
                c.ChunkIndex,
                c.EmbeddingModel,
                c.Content,
                c.CreatedAtTicks
            })
            .ToListAsync(ct);

        var chunks = rawChunks.Select(c => new
        {
            c.Id,
            c.DocumentId,
            c.DocumentName,
            c.ChunkIndex,
            c.EmbeddingModel,
            ContentPreview = c.Content.Length > 100 ? c.Content[..100] + "..." : c.Content,
            CreatedAt = new DateTimeOffset(c.CreatedAtTicks, TimeSpan.Zero)
        });

        return Ok(new { total, page, size, items = chunks });
    }

    /// <summary>
    /// Clears all vector data and sync state. Requires the X-Confirm: yes header to prevent accidental deletion.
    /// </summary>
    [HttpDelete("data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearData(
        [FromHeader(Name = "X-Confirm")] string? confirm,
        CancellationToken ct)
    {
        if (!string.Equals(confirm, "yes", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Include 'X-Confirm: yes' header to confirm deletion." });

        var deletedChunks = await vectorStore.ClearAllAsync(ct);
        var deletedFiles  = await db.SyncedFiles.ExecuteDeleteAsync(ct);

        logger.LogWarning("Admin: cleared {Chunks} chunks and {Files} sync records", deletedChunks, deletedFiles);

        return Ok(new
        {
            message = "All vector data and sync state cleared.",
            deletedChunks,
            deletedSyncRecords = deletedFiles
        });
    }

    /// <summary>Clears the semantic cache.</summary>
    [HttpDelete("cache")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearCache(CancellationToken ct)
    {
        await semanticCache.ClearAsync(ct);
        logger.LogInformation("Admin: semantic cache cleared");
        return Ok(new { message = "Semantic cache cleared." });
    }

    /// <summary>Deletes all chunks of the specified document.</summary>
    [HttpDelete("documents/{documentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteDocument(string documentId, CancellationToken ct)
    {
        await vectorStore.DeleteByDocumentAsync(documentId, ct);
        logger.LogInformation("Admin: deleted document {DocumentId}", documentId);

        return Ok(new { message = $"Document '{documentId}' deleted." });
    }

    /// <summary>Returns version history for the given document name (including superseded versions).</summary>
    [HttpGet("documents/{documentName}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentHistory(string documentName, CancellationToken ct)
    {
        var history = await vectorStore.GetVersionHistoryAsync(documentName, ct);
        if (history.Count == 0)
            return NotFound(new { error = $"Document '{documentName}' not found." });

        return Ok(new
        {
            documentName,
            versionCount = history.Count,
            versions = history.Select(v => new
            {
                v.DocumentId,
                v.Version,
                v.ChunkCount,
                v.CreatedAt,
                v.SupersededAt,
                isCurrent = v.SupersededAt is null
            })
        });
    }
}
