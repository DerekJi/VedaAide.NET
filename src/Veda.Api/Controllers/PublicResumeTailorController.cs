using Veda.Core.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Veda.Api.Controllers;

/// <summary>
/// 公开简历定制端点：POST /api/public/resume/tailor
/// 无需登录，专为 resume 站（derekji.github.io）提供的免认证 SSE 接口。
/// 防滥用：CORS 白名单（仅允许 resume 站 origin）+ per-IP 固定窗口限流。
/// </summary>
[ApiController]
[Route("api/public/resume")]
[AllowAnonymous]
[EnableCors("ResumePublicPolicy")]
[EnableRateLimiting("resume-public")]
public sealed class PublicResumeTailorController(
    IPublicResumeTailoringService tailoringService,
    IOptions<VedaOptions> options) : ControllerBase
{
    [HttpPost("tailor")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task Tailor([FromBody] PublicTailorRequest request, CancellationToken ct)
    {
        var maxChars = options.Value.PublicResume.MaxJobDescriptionChars;
        if (string.IsNullOrWhiteSpace(request.JobDescription))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "jobDescription is required." }, ct);
            return;
        }

        if (request.JobDescription.Length > maxChars)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = $"jobDescription must not exceed {maxChars} characters." }, ct);
            return;
        }

        var topK = Math.Clamp(request.TopK, 3, 15);

        Response.Headers.ContentType  = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection   = "keep-alive";

        await foreach (var token in tailoringService.TailorStreamAsync(request.JobDescription, topK, ct))
        {
            await Response.WriteAsync(token, ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}

/// <summary>公开简历定制请求体。</summary>
public record PublicTailorRequest(
    string JobDescription,
    int TopK = 8);
