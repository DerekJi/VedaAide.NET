using Veda.Core.Options;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Veda.Api.Models;
using Veda.Services;

namespace Veda.Api.Controllers;

/// <summary>
/// SSE 流式问答端点：GET /api/querystream?question=...
/// 前端通过 EventSource 订阅，依次收到 sources → token × N → done 三类事件。
/// POST /api/querystream：携带临时附件上下文（Ephemeral RAG）时使用。
/// </summary>
[ApiController]
[Route("api/querystream")]
[Authorize]
public class QueryStreamController(IQueryStreamService queryStreamService, IOptions<RagOptions> ragOptions) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 流式问答（Server-Sent Events）。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task Stream(
        [FromQuery] string question,
        [FromQuery] string? documentType = null,
        [FromQuery] int topK = 5,
        [FromQuery] float? minSimilarity = null,
        [FromQuery] DateTimeOffset? dateFrom = null,
        [FromQuery] DateTimeOffset? dateTo = null,
        [FromQuery] QueryMode mode = QueryMode.Simple,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var currentUser = HttpContext.RequestServices.GetRequiredService<ICurrentUserService>();

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var request = new RagQueryRequest
        {
            Question = question,
            FilterDocumentType = DocumentTypeParser.ParseOrNull(documentType),
            TopK = topK,
            MinSimilarity = minSimilarity ?? ragOptions.Value.DefaultMinSimilarity,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Mode = mode,
            UserId = currentUser.UserId,
            Scope = currentUser.UserId is not null
                ? new KnowledgeScope(OwnerId: currentUser.UserId)
                : null
        };

        await WriteStreamAsync(request, ct);
    }

    /// <summary>
    /// 携带临时附件上下文的流式问答（Context Augmentation / Ephemeral RAG）。
    /// 前端上传文件提取文本后，将提取结果放入 <see cref="QueryStreamRequest.ExtraContext"/> 字段随请求发送。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task StreamWithContext([FromBody] QueryStreamRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Question))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var currentUser = HttpContext.RequestServices.GetRequiredService<ICurrentUserService>();

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var request = new RagQueryRequest
        {
            Question         = body.Question,
            TopK             = body.TopK,
            MinSimilarity    = body.MinSimilarity,
            DateFrom         = body.DateFrom,
            DateTo           = body.DateTo,
            Mode             = body.Mode,
            EphemeralContext = body.ExtraContext,
            UserId           = currentUser.UserId,
            Scope            = currentUser.UserId is not null
                ? new KnowledgeScope(OwnerId: currentUser.UserId)
                : null
        };

        await WriteStreamAsync(request, ct);
    }

    private async Task WriteStreamAsync(RagQueryRequest request, CancellationToken ct)
    {
        await foreach (var chunk in queryStreamService.QueryStreamAsync(request, ct))
        {
            var data = JsonSerializer.Serialize(chunk, JsonOptions);
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
