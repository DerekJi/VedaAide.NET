using Microsoft.AspNetCore.Authorization;
using Veda.Core.Interfaces;

namespace Veda.Api.Controllers;

/// <summary>
/// Token 消耗统计端点。
/// GET /api/usage/summary — 返回当前用户本月及历史累计消耗。
/// Admin 可通过 ?userId=xxx 查询其他用户数据。
/// </summary>
[ApiController]
[Route("api/usage")]
[Authorize]
public class UsageController(
    ITokenUsageRepository usageRepo,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string? userId = null,
        CancellationToken ct = default)
    {
        // 普通用户只能查自己；Admin 可指定 userId
        var targetUserId = (currentUser.IsAdmin && !string.IsNullOrWhiteSpace(userId))
            ? userId
            : currentUser.UserId;

        if (string.IsNullOrWhiteSpace(targetUserId))
            return Unauthorized();

        var summary = await usageRepo.GetSummaryAsync(targetUserId, ct);
        return Ok(summary);
    }
}
