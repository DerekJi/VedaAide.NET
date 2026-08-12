using Veda.Core.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Veda.Api.Controllers;

/// <summary>
/// Public resume tailoring endpoint: POST /api/public/resume/tailor
/// No login required; an unauthenticated SSE interface built for the resume site (derekji.github.io).
/// Abuse prevention: CORS whitelist (only the resume site origin) + per-IP fixed-window rate limiting.
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
    /// <summary>
    /// Lightweight health probe used by the frontend to detect whether the Container App has recovered from a cold start.
    /// GET /api/public/resume/ping → 200 OK { "status": "ok" }
    /// </summary>
    [HttpGet("ping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Ping() => Ok(new { status = "ok" });

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

/// <summary>Public resume tailoring request body.</summary>
public record PublicTailorRequest(
    string JobDescription,
    int TopK = 8);
