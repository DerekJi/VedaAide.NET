using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// 用户记忆层服务——基于 SQLite 的轻量实现。
/// 生产环境可替换为 CosmosDB 实现（UserBehaviors 容器）。
/// 符合隐私设计：仅存储匿名 chunkId + userId，不记录文档原始内容。
/// </summary>
public sealed class UserMemoryStore(
    VedaDbContext db,
    ILogger<UserMemoryStore> logger) : IUserMemoryStore
{
    private const float BoostPerAccept = 0.2f;
    private const float PenaltyPerReject = 0.15f;
    private const float BoostCap = 2.0f;
    private const float BoostFloor = 0.3f;

    public async Task RecordEventAsync(UserBehaviorEvent evt, CancellationToken ct = default)
    {
        var entity = new UserBehaviorEntity
        {
            Id = evt.Id,
            UserId = evt.UserId,
            SessionId = evt.SessionId,
            Type = (int)evt.Type,
            RelatedChunkId = evt.RelatedChunkId ?? string.Empty,
            RelatedDocumentId = evt.RelatedDocumentId ?? string.Empty,
            Query = evt.Query ?? string.Empty,
            OccurredAtTicks = evt.OccurredAt.UtcTicks
        };
        db.UserBehaviors.Add(entity);
        await db.SaveChangesAsync(ct);
        logger.LogDebug("Recorded behavior event {Type} for user {UserId}", evt.Type, evt.UserId);
    }

    public async Task<float> GetBoostFactorAsync(string userId, string chunkId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(chunkId))
            return 1.0f;

        var accepts = await db.UserBehaviors.CountAsync(
            e => e.UserId == userId && e.RelatedChunkId == chunkId && e.Type == (int)BehaviorType.ResultAccepted, ct);
        var rejects = await db.UserBehaviors.CountAsync(
            e => e.UserId == userId && e.RelatedChunkId == chunkId && e.Type == (int)BehaviorType.ResultRejected, ct);

        if (accepts == 0 && rejects == 0) return 1.0f;

        var boost = 1.0f + (accepts * BoostPerAccept) - (rejects * PenaltyPerReject);
        return Math.Clamp(boost, BoostFloor, BoostCap);
    }

    public async Task<IReadOnlyDictionary<string, float>> GetTermPreferencesAsync(
        string userId, CancellationToken ct = default)
    {
        var acceptedQueries = await db.UserBehaviors
            .Where(e => e.UserId == userId && e.Type == (int)BehaviorType.ResultAccepted && e.Query.Length > 0)
            .Select(e => e.Query)
            .ToListAsync(ct);

        var termCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in acceptedQueries)
        {
            foreach (var term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                      .Where(t => t.Length >= 2))
            {
                termCounts[term] = termCounts.GetValueOrDefault(term) + 1;
            }
        }

        var total = termCounts.Values.Sum();
        if (total == 0) return new Dictionary<string, float>();

        return termCounts.ToDictionary(kv => kv.Key, kv => (float)kv.Value / total,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<FeedbackStats> GetStatsAsync(CancellationToken ct = default)
    {
        var total = await db.UserBehaviors.CountAsync(ct);
        var accepted = await db.UserBehaviors.CountAsync(e => e.Type == (int)BehaviorType.ResultAccepted, ct);
        var rejected = await db.UserBehaviors.CountAsync(e => e.Type == (int)BehaviorType.ResultRejected, ct);

        var topRejected = await db.UserBehaviors
            .Where(e => e.Type == (int)BehaviorType.ResultRejected && e.RelatedChunkId.Length > 0)
            .GroupBy(e => e.RelatedChunkId)
            .Select(g => new { ChunkId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        var result = topRejected.Select(x => new RejectedChunkInfo(x.ChunkId, null, x.Count)).ToList();

        // Enrich with document names if available
        if (result.Count > 0)
        {
            var chunkIds = result.Select(r => r.ChunkId).ToList();
            var docNames = await db.VectorChunks
                .Where(c => chunkIds.Contains(c.Id))
                .Select(c => new { c.Id, c.DocumentName })
                .ToListAsync(ct);
            var nameMap = docNames.ToDictionary(x => x.Id, x => x.DocumentName);
            result = result.Select(r => r with { DocumentName = nameMap.GetValueOrDefault(r.ChunkId) }).ToList();
        }

        return new FeedbackStats(total, accepted, rejected, result);
    }
}
