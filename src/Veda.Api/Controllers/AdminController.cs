using Microsoft.EntityFrameworkCore;
using Veda.Core.Interfaces;
using Veda.Storage;

namespace Veda.Api.Controllers;

/// <summary>
/// 开发管理端点，需 Admin API Key（Veda:Security:AdminApiKey）。
/// 支持查看 DB 状态、分页浏览 chunks、清空数据、删除指定文档。
/// CosmosDB 模式：stats/clear 通过 IVectorStore 接口操作；chunks 分页仅支持 SQLite。
/// </summary>
[ApiController]
[Route("api/admin")]
public sealed class AdminController(
    IVectorStore vectorStore,
    ISemanticCache semanticCache,
    VedaDbContext db,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>返回向量库统计信息（chunk 总数、文档数）。</summary>
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

        return Ok(new
        {
            chunkCount,
            documentCount = docCount,
            syncedFileCount = syncedFiles
        });
    }

    /// <summary>分页查看所有 chunks（SQLite 模式专用）。</summary>
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
    /// 清空所有向量数据和同步状态。需附加 X-Confirm: yes 请求头防误操作。
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

        var deletedChunks = await db.VectorChunks.ExecuteDeleteAsync(ct);
        var deletedFiles  = await db.SyncedFiles.ExecuteDeleteAsync(ct);

        logger.LogWarning("Admin: cleared {Chunks} chunks and {Files} sync records", deletedChunks, deletedFiles);

        return Ok(new
        {
            message = "All vector data and sync state cleared.",
            deletedChunks,
            deletedSyncRecords = deletedFiles
        });
    }

    /// <summary>清空语义缓存。</summary>
    [HttpDelete("cache")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearCache(CancellationToken ct)
    {
        await semanticCache.ClearAsync(ct);
        logger.LogInformation("Admin: semantic cache cleared");
        return Ok(new { message = "Semantic cache cleared." });
    }

    /// <summary>删除指定文档的所有 chunks。</summary>
    [HttpDelete("documents/{documentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(string documentId, CancellationToken ct)
    {
        var exists = await db.VectorChunks.AnyAsync(c => c.DocumentId == documentId, ct);
        if (!exists)
            return NotFound(new { error = $"Document '{documentId}' not found." });

        await vectorStore.DeleteByDocumentAsync(documentId, ct);
        logger.LogInformation("Admin: deleted document {DocumentId}", documentId);

        return Ok(new { message = $"Document '{documentId}' deleted." });
    }
}
