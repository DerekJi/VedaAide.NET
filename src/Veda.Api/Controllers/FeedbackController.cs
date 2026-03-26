using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Api.Controllers;

/// <summary>
/// 用户行为反馈端点。
/// 前端上报用户互动行为（采纳/拒绝/修改等），用于个性化检索权重计算。
/// </summary>
[ApiController]
[Route("api/feedback")]
public sealed class FeedbackController(
    IUserMemoryStore userMemoryStore,
    ILogger<FeedbackController> logger) : ControllerBase
{
    /// <summary>上报用户行为事件。</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordFeedback(
        [FromBody] FeedbackRequest request,
        CancellationToken ct)
    {
        if (!Enum.TryParse<BehaviorType>(request.Type, ignoreCase: true, out var behaviorType))
            return BadRequest(new { error = $"Unknown behavior type: {request.Type}" });

        var evt = new UserBehaviorEvent(
            UserId: request.UserId,
            SessionId: request.SessionId ?? Guid.NewGuid().ToString(),
            Type: behaviorType,
            RelatedDocumentId: request.RelatedDocumentId,
            RelatedChunkId: request.RelatedChunkId,
            Query: request.Query,
            OccurredAt: DateTimeOffset.UtcNow);

        await userMemoryStore.RecordEventAsync(evt, ct);
        logger.LogDebug("Feedback recorded: {Type} for user {UserId}", behaviorType, request.UserId);

        return Accepted(new { message = "Feedback recorded." });
    }

    /// <summary>获取反馈统计（管理员端点）。</summary>
    [HttpGet("stats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var stats = await userMemoryStore.GetStatsAsync(ct);
        return Ok(stats);
    }
}

public record FeedbackRequest(
    string UserId,
    string Type,
    string? SessionId = null,
    string? RelatedDocumentId = null,
    string? RelatedChunkId = null,
    string? Query = null);
