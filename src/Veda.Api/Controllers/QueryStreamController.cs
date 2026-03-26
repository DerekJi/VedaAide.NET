using System.Text.Json;
using Microsoft.Extensions.Options;
using Veda.Api.Models;
using Veda.Services;

namespace Veda.Api.Controllers;

/// <summary>
/// SSE 流式问答端点：GET /api/querystream?question=...
/// 前端通过 EventSource 订阅，依次收到 sources → token × N → done 三类事件。
/// </summary>
[ApiController]
[Route("api/querystream")]
public class QueryStreamController(IQueryService queryService, IOptions<RagOptions> ragOptions) : ControllerBase
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
            Mode = mode
        };

        await foreach (var chunk in queryService.QueryStreamAsync(request, ct))
        {
            var data = JsonSerializer.Serialize(chunk, JsonOptions);
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
