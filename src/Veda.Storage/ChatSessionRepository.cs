using System.Text.Json;
using Veda.Core.Interfaces;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// SQLite-backed 会话持久化仓储。userId 作为强制查询条件，确保用户数据隔离。
/// </summary>
public sealed class ChatSessionRepository(VedaDbContext db) : IChatSessionRepository
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<ChatSessionRecord> CreateAsync(string userId, string title, CancellationToken ct = default)
    {
        var entity = new ChatSessionEntity
        {
            SessionId = Guid.NewGuid().ToString(),
            UserId    = userId,
            Title     = string.IsNullOrWhiteSpace(title) ? "New Chat" : title,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.ChatSessions.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToRecord(entity);
    }

    public async Task<IReadOnlyList<ChatSessionRecord>> ListAsync(string userId, CancellationToken ct = default)
    {
        return await db.ChatSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new ChatSessionRecord(s.SessionId, s.UserId, s.Title, s.CreatedAt, s.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(string sessionId, string userId, CancellationToken ct = default)
    {
        var entity = await db.ChatSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

        if (entity is null) return;

        // Return 403-equivalent if session belongs to another user
        if (entity.UserId != userId)
            throw new UnauthorizedAccessException($"Session '{sessionId}' does not belong to user '{userId}'.");

        db.ChatSessions.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task AppendMessageAsync(ChatMessageRecord message, CancellationToken ct = default)
    {
        // Verify session ownership before appending
        var session = await db.ChatSessions
            .FirstOrDefaultAsync(s => s.SessionId == message.SessionId, ct);

        if (session is null || session.UserId != message.UserId)
            throw new UnauthorizedAccessException($"Session '{message.SessionId}' does not belong to user '{message.UserId}'.");

        var entity = new ChatMessageEntity
        {
            MessageId       = message.MessageId,
            SessionId       = message.SessionId,
            UserId          = message.UserId,
            Role            = message.Role,
            Content         = message.Content.Length > 10_000
                                ? message.Content[..10_000]
                                : message.Content,
            Confidence      = message.Confidence,
            IsHallucination = message.IsHallucination,
            SourcesJson     = JsonSerializer.Serialize(message.Sources, _json),
            CreatedAt       = message.CreatedAt
        };
        db.ChatMessages.Add(entity);

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(string sessionId, string userId, CancellationToken ct = default)
    {
        var session = await db.ChatSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

        if (session is null || session.UserId != userId)
            throw new UnauthorizedAccessException($"Session '{sessionId}' does not belong to user '{userId}'.");

        return await db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => ToRecord(m))
            .ToListAsync(ct);
    }

    private static ChatSessionRecord ToRecord(ChatSessionEntity e) =>
        new(e.SessionId, e.UserId, e.Title, e.CreatedAt, e.UpdatedAt);

    private static ChatMessageRecord ToRecord(ChatMessageEntity m)
    {
        List<ChatSourceRef> sources;
        try { sources = JsonSerializer.Deserialize<List<ChatSourceRef>>(m.SourcesJson, _json) ?? []; }
        catch  { sources = []; }

        return new(m.MessageId, m.SessionId, m.UserId, m.Role, m.Content,
                   m.Confidence, m.IsHallucination, sources, m.CreatedAt);
    }
}
