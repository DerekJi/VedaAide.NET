using Microsoft.AspNetCore.Authorization;
using Veda.Core.Interfaces;

namespace Veda.Api.Controllers;

/// <summary>
/// 知识治理管理端点。
/// 管理共享组、文档授权共享、共识候选审核。
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/governance")]
public sealed class GovernanceController(
    IKnowledgeGovernanceService governanceService,
    ILogger<GovernanceController> logger) : ControllerBase
{
    /// <summary>创建知识共享组。</summary>
    [HttpPost("groups")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateGroup(
        [FromBody] CreateGroupRequest request,
        CancellationToken ct)
    {
        var groupId = await governanceService.CreateSharingGroupAsync(
            request.OwnerId, request.MemberIds, ct);
        logger.LogInformation("Sharing group {GroupId} created", groupId);
        return Created($"/api/governance/groups/{groupId}", new { groupId });
    }

    /// <summary>授权文档对共享组可见。</summary>
    [HttpPut("documents/{documentId}/share")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ShareDocument(
        string documentId,
        [FromBody] ShareDocumentRequest request,
        CancellationToken ct)
    {
        try
        {
            await governanceService.ShareDocumentAsync(documentId, request.OwnerId, request.GroupId, ct);
            return Ok(new { message = $"Document '{documentId}' shared with group '{request.GroupId}'." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>获取待审核的共识候选列表（管理员）。</summary>
    [HttpGet("consensus/pending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingCandidates(CancellationToken ct)
    {
        var candidates = await governanceService.GetPendingCandidatesAsync(ct);
        return Ok(candidates);
    }

    /// <summary>审核共识候选（管理员）。</summary>
    [HttpPost("consensus/{candidateId}/review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReviewCandidate(
        string candidateId,
        [FromBody] ReviewCandidateRequest request,
        CancellationToken ct)
    {
        var success = await governanceService.ReviewConsensusAsync(
            candidateId, request.Approved, request.ReviewerId, ct);
        if (!success)
            return NotFound(new { error = $"Candidate '{candidateId}' not found." });

        return Ok(new { message = $"Candidate {(request.Approved ? "approved" : "rejected")}." });
    }

    /// <summary>检查文档对用户是否可见（隐私隔离验证）。</summary>
    [HttpGet("documents/{documentId}/visible")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckVisibility(
        string documentId,
        [FromQuery] string userId,
        CancellationToken ct)
    {
        var visible = await governanceService.IsDocumentVisibleToUserAsync(documentId, userId, ct);
        return Ok(new { documentId, userId, visible });
    }
}

public record CreateGroupRequest(
    string OwnerId,
    IReadOnlyList<string> MemberIds);

public record ShareDocumentRequest(
    string OwnerId,
    string GroupId);

public record ReviewCandidateRequest(
    bool Approved,
    string ReviewerId);
