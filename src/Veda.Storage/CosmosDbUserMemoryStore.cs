using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Storage;

/// <summary>
/// User memory store — CosmosDB cloud implementation.
/// Persists user feedback behavior events in the UserBehaviors container, partitioned by /userId.
/// </summary>
public sealed class CosmosDbUserMemoryStore(
    CosmosClient client,
    CosmosDbOptions options,
    ILogger<CosmosDbUserMemoryStore> logger) : IUserMemoryStore
{
    private const float BoostPerAccept  = 0.2f;
    private const float PenaltyPerReject = 0.15f;
    private const float BoostCap  = 2.0f;
    private const float BoostFloor = 0.3f;

    private Container Container =>
        client.GetDatabase(options.DatabaseName).GetContainer(options.BehaviorsContainerName);

    public async Task RecordEventAsync(UserBehaviorEvent evt, CancellationToken ct = default)
    {
        var doc = new
        {
            id                = evt.Id,
            userId            = evt.UserId,
            sessionId         = evt.SessionId,
            type              = (int)evt.Type,
            relatedChunkId    = evt.RelatedChunkId    ?? string.Empty,
            relatedDocumentId = evt.RelatedDocumentId ?? string.Empty,
            query             = evt.Query             ?? string.Empty,
            occurredAtTicks   = evt.OccurredAt.UtcTicks
        };
        await Container.CreateItemAsync(doc, new PartitionKey(evt.UserId), cancellationToken: ct);
        logger.LogDebug("Recorded behavior event {Type} for user {UserId}", evt.Type, evt.UserId);
    }

    public async Task<float> GetBoostFactorAsync(string userId, string chunkId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(chunkId))
            return 1.0f;

        var opts = new QueryRequestOptions { PartitionKey = new PartitionKey(userId) };

        var accepts = await CountAsync(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.userId = @u AND c.relatedChunkId = @c AND c.type = @t")
                .WithParameter("@u", userId)
                .WithParameter("@c", chunkId)
                .WithParameter("@t", (int)BehaviorType.ResultAccepted),
            opts, ct);

        var rejects = await CountAsync(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.userId = @u AND c.relatedChunkId = @c AND c.type = @t")
                .WithParameter("@u", userId)
                .WithParameter("@c", chunkId)
                .WithParameter("@t", (int)BehaviorType.ResultRejected),
            opts, ct);

        if (accepts == 0 && rejects == 0) return 1.0f;

        var boost = 1.0f + (accepts * BoostPerAccept) - (rejects * PenaltyPerReject);
        return Math.Clamp(boost, BoostFloor, BoostCap);
    }

    public async Task<IReadOnlyDictionary<string, float>> GetTermPreferencesAsync(
        string userId, CancellationToken ct = default)
    {
        var queryDef = new QueryDefinition(
            "SELECT c.query FROM c WHERE c.userId = @u AND c.type = @t AND LENGTH(c.query) > 0")
            .WithParameter("@u", userId)
            .WithParameter("@t", (int)BehaviorType.ResultAccepted);

        var queries = new List<string>();
        var iter = Container.GetItemQueryIterator<QueryProjection>(queryDef,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        while (iter.HasMoreResults)
        {
            var page = await iter.ReadNextAsync(ct);
            queries.AddRange(page.Select(x => x.query));
        }

        var termCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in queries)
        {
            foreach (var term in q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 2))
                termCounts[term] = termCounts.GetValueOrDefault(term) + 1;
        }

        var total = termCounts.Values.Sum();
        if (total == 0) return new Dictionary<string, float>();

        return termCounts.ToDictionary(kv => kv.Key, kv => (float)kv.Value / total, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<FeedbackStats> GetStatsAsync(CancellationToken ct = default)
    {
        // Cross-partition aggregation queries
        var total    = await CountAsync(new QueryDefinition("SELECT VALUE COUNT(1) FROM c"), null, ct);
        var accepted = await CountAsync(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.type = @t")
                .WithParameter("@t", (int)BehaviorType.ResultAccepted), null, ct);
        var rejected = await CountAsync(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.type = @t")
                .WithParameter("@t", (int)BehaviorType.ResultRejected), null, ct);

        // Fetch all rejected chunk IDs and aggregate in memory (top-10 stats, not hot path)
        var rejectedChunkQuery = new QueryDefinition(
            "SELECT c.relatedChunkId FROM c WHERE c.type = @t AND LENGTH(c.relatedChunkId) > 0")
            .WithParameter("@t", (int)BehaviorType.ResultRejected);

        var chunkIds = new List<string>();
        var iter = Container.GetItemQueryIterator<ChunkIdProjection>(rejectedChunkQuery);
        while (iter.HasMoreResults)
        {
            var page = await iter.ReadNextAsync(ct);
            chunkIds.AddRange(page.Select(x => x.relatedChunkId));
        }

        var topRejected = chunkIds
            .GroupBy(id => id)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new RejectedChunkInfo(g.Key, null, g.Count()))
            .ToList();

        return new FeedbackStats(total, accepted, rejected, topRejected);
    }

    private async Task<int> CountAsync(QueryDefinition queryDef, QueryRequestOptions? opts, CancellationToken ct)
    {
        var iter = Container.GetItemQueryIterator<int>(queryDef, requestOptions: opts);
        if (!iter.HasMoreResults) return 0;
        var page = await iter.ReadNextAsync(ct);
        return page.FirstOrDefault();
    }

    private record QueryProjection(string query);
    private record ChunkIdProjection(string relatedChunkId);
}
