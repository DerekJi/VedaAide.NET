using Microsoft.AspNetCore.Authorization;
using Veda.Core.Interfaces;

namespace Veda.Api.Controllers;

/// <summary>
/// Token usage statistics endpoints.
/// GET /api/usage/summary — returns the current user's consumption for this month and cumulative history.
/// Admins can query other users' data via ?userId=xxx.
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
        // Regular users can only query themselves; Admins can specify a userId
        var targetUserId = (currentUser.IsAdmin && !string.IsNullOrWhiteSpace(userId))
            ? userId
            : currentUser.UserId;

        if (string.IsNullOrWhiteSpace(targetUserId))
            return Unauthorized();

        var summary = await usageRepo.GetSummaryAsync(targetUserId, ct);
        return Ok(summary);
    }
}
