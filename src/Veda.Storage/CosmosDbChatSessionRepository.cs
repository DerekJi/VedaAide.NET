using Veda.Core.Options;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Storage;

/// <summary>
/// CosmosDB-backed 会话持久化仓储。
/// Partition Key = /userId，天然隔离不同用户数据；所有查询均携带 PartitionKey 约束。
/// </summary>
public sealed class CosmosDbChatSessionRepository(
    CosmosClient client,
    CosmosDbOptions options,
    ILogger<CosmosDbChatSessionRepository> logger) : IChatSessionRepository
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private Container Sessions =>
        client.GetDatabase(options.DatabaseName).GetContainer(options.ChatSessionsContainerName);

    // ── Session operations ───────────────────────────────────────────────────

    public async Task<ChatSessionRecord> CreateAsync(string userId, string title, CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var now       = DateTimeOffset.UtcNow;
        var doc = new SessionDoc(
            id:        sessionId,
            type:      "session",
            userId:    userId,
            title:     string.IsNullOrWhiteSpace(title) ? "New Chat" : title,
            createdAt: now,
            updatedAt: now);

        await Sessions.CreateItemAsync(doc, new PartitionKey(userId), cancellationToken: ct);
        logger.LogDebug("Created CosmosDB chat session {Id} for user {User}", sessionId, userId);
        return new ChatSessionRecord(sessionId, userId, doc.title, now, now);
    }

    public async Task<IReadOnlyList<ChatSessionRecord>> ListAsync(string userId, CancellationToken ct = default)
    {
        var queryDef = new QueryDefinition(
            "SELECT c.id, c.userId, c.title, c.createdAt, c.updatedAt " +
            "FROM c WHERE c.userId = @u AND c.type = 'session' " +
            "ORDER BY c.updatedAt DESC")
            .WithParameter("@u", userId);

        var results = new List<ChatSessionRecord>();
        var iter = Sessions.GetItemQueryIterator<SessionDoc>(queryDef,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        while (iter.HasMoreResults)
        {
            var page = await iter.ReadNextAsync(ct);
            foreach (var doc in page)
                results.Add(new ChatSessionRecord(doc.id, doc.userId, doc.title, doc.createdAt, doc.updatedAt));
        }
        return results;
    }

    public async Task DeleteAsync(string sessionId, string userId, CancellationToken ct = default)
    {
        // Fetch the session document first to verify ownership
        SessionDoc? session = null;
        try
        {
            var response = await Sessions.ReadItemAsync<SessionDoc>(
                sessionId, new PartitionKey(userId), cancellationToken: ct);
            session = response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return; // Already gone — idempotent
        }

        if (session.userId != userId)
            throw new UnauthorizedAccessException($"Session '{sessionId}' does not belong to user '{userId}'.");

        // Delete all messages in this session (type='message')
        var msgQuery = new QueryDefinition(
            "SELECT c.id FROM c WHERE c.sessionId = @s AND c.type = 'message'")
            .WithParameter("@s", sessionId);

        var msgIter = Sessions.GetItemQueryIterator<IdOnly>(msgQuery,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        while (msgIter.HasMoreResults)
        {
            var page = await msgIter.ReadNextAsync(ct);
            foreach (var m in page)
            {
                try { await Sessions.DeleteItemAsync<object>(m.id, new PartitionKey(userId), cancellationToken: ct); }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
            }
        }

        // Delete the session document itself
        try
        {
            await Sessions.DeleteItemAsync<object>(sessionId, new PartitionKey(userId), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }

        logger.LogDebug("Deleted CosmosDB chat session {Id} for user {User}", sessionId, userId);
    }

    // ── Message operations ───────────────────────────────────────────────────

    public async Task AppendMessageAsync(ChatMessageRecord message, CancellationToken ct = default)
    {
        // Verify session ownership before appending
        try
        {
            var resp = await Sessions.ReadItemAsync<SessionDoc>(
                message.SessionId, new PartitionKey(message.UserId), cancellationToken: ct);
            if (resp.Resource.userId != message.UserId)
                throw new UnauthorizedAccessException($"Session '{message.SessionId}' does not belong to user '{message.UserId}'.");
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new UnauthorizedAccessException($"Session '{message.SessionId}' does not belong to user '{message.UserId}'.");
        }

        var doc = new MessageDoc(
            id:             message.MessageId,
            type:           "message",
            sessionId:      message.SessionId,
            userId:         message.UserId,
            role:           message.Role,
            content:        message.Content.Length > 10_000 ? message.Content[..10_000] : message.Content,
            confidence:     message.Confidence,
            isHallucination: message.IsHallucination,
            sourcesJson:    JsonSerializer.Serialize(message.Sources, _json),
            createdAt:      message.CreatedAt);

        await Sessions.CreateItemAsync(doc, new PartitionKey(message.UserId), cancellationToken: ct);

        // Update session updatedAt via patch
        await Sessions.PatchItemAsync<object>(
            message.SessionId,
            new PartitionKey(message.UserId),
            [PatchOperation.Set("/updatedAt", DateTimeOffset.UtcNow)],
            cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(
        string sessionId, string userId, CancellationToken ct = default)
    {
        // Verify session ownership
        try
        {
            var resp = await Sessions.ReadItemAsync<SessionDoc>(
                sessionId, new PartitionKey(userId), cancellationToken: ct);
            if (resp.Resource.userId != userId)
                throw new UnauthorizedAccessException($"Session '{sessionId}' does not belong to user '{userId}'.");
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new UnauthorizedAccessException($"Session '{sessionId}' does not belong to user '{userId}'.");
        }

        var queryDef = new QueryDefinition(
            "SELECT * FROM c WHERE c.sessionId = @s AND c.type = 'message' ORDER BY c.createdAt ASC")
            .WithParameter("@s", sessionId);

        var results = new List<ChatMessageRecord>();
        var iter = Sessions.GetItemQueryIterator<MessageDoc>(queryDef,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        while (iter.HasMoreResults)
        {
            var page = await iter.ReadNextAsync(ct);
            foreach (var doc in page)
                results.Add(ToRecord(doc));
        }
        return results;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static ChatMessageRecord ToRecord(MessageDoc m)
    {
        List<ChatSourceRef> sources;
        try { sources = JsonSerializer.Deserialize<List<ChatSourceRef>>(m.sourcesJson, _json) ?? []; }
        catch { sources = []; }

        return new(m.id, m.sessionId, m.userId, m.role, m.content,
                   m.confidence, m.isHallucination, sources, m.createdAt);
    }

    // ── Internal document shapes ─────────────────────────────────────────────

    private record SessionDoc(
        string id,
        string type,
        string userId,
        string title,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt);

    private record MessageDoc(
        string id,
        string type,
        string sessionId,
        string userId,
        string role,
        string content,
        float? confidence,
        bool isHallucination,
        string sourcesJson,
        DateTimeOffset createdAt);

    private record IdOnly(string id);
}
