using Microsoft.AspNetCore.Authorization;
using Veda.Api.Models;
using Veda.Core.Interfaces;

namespace Veda.Api.Controllers;

/// <summary>
/// 会话持久化端点。所有端点从 JWT 提取身份，不信任请求体中的 userId。
/// </summary>
[ApiController]
[Route("api/chat/sessions")]
[Authorize]
public sealed class ChatSessionController(
    IChatSessionRepository repo,
    ICurrentUserService currentUser,
    ILogger<ChatSessionController> logger) : ControllerBase
{
    // ── Create ────────────────────────────────────────────────────────────────

    [HttpPost]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateSession(
        [FromBody] CreateSessionRequest request,
        CancellationToken ct)
    {
        var userId = RequireUserId();
        var record = await repo.CreateAsync(userId, request.Title ?? "New Chat", ct);
        logger.LogDebug("Created chat session {Id} for user {User}", record.SessionId, userId);
        return CreatedAtAction(nameof(GetMessages), new { id = record.SessionId }, ToDto(record));
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SessionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var userId = RequireUserId();
        var sessions = await repo.ListAsync(userId, ct);
        return Ok(sessions.Select(ToDto));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteSession(string id, CancellationToken ct)
    {
        var userId = RequireUserId();
        try
        {
            await repo.DeleteAsync(id, userId, ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            // Return 403, not 404, to avoid leaking whether the session exists
            return Forbid();
        }
    }

    // ── Get messages ──────────────────────────────────────────────────────────

    [HttpGet("{id}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessages(string id, CancellationToken ct)
    {
        var userId = RequireUserId();
        try
        {
            var messages = await repo.GetMessagesAsync(id, userId, ct);
            return Ok(messages.Select(ToDto));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // ── Append message ────────────────────────────────────────────────────────

    [HttpPost("{id}/messages")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AppendMessage(
        string id,
        [FromBody] AppendMessageRequest request,
        CancellationToken ct)
    {
        var userId = RequireUserId();

        var record = new ChatMessageRecord(
            MessageId:       Guid.NewGuid().ToString(),
            SessionId:       id,
            UserId:          userId,
            Role:            request.Role,
            Content:         request.Content,
            Confidence:      request.Confidence,
            IsHallucination: request.IsHallucination,
            Sources:         request.Sources?.Select(s => new ChatSourceRef(
                                 s.DocumentName, s.ChunkContent, s.Similarity, s.ChunkId, s.DocumentId))
                             .ToList() ?? [],
            CreatedAt:       DateTimeOffset.UtcNow);

        try
        {
            await repo.AppendMessageAsync(record, ct);
            return CreatedAtAction(nameof(GetMessages), new { id }, ToDto(record));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("JWT UserId claim is missing.");

    private static SessionResponse ToDto(ChatSessionRecord r) =>
        new(r.SessionId, r.Title, r.CreatedAt, r.UpdatedAt);

    private static MessageResponse ToDto(ChatMessageRecord m) =>
        new(m.MessageId, m.SessionId, m.Role, m.Content, m.Confidence, m.IsHallucination,
            m.Sources.Select(s => new ChatSourceRefDto(s.DocumentName, s.ChunkContent, s.Similarity, s.ChunkId, s.DocumentId)).ToList(),
            m.CreatedAt);
}
