using Veda.Core.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Storage;

/// <summary>
/// 基于 Azure CosmosDB for NoSQL 的语义缓存实现。
/// 使用单独的容器（SemanticCache）存储问题 embedding 与答案。
/// 暂时通过内存余弦相似度匹配（CosmosDB DiskANN 需要专用容器向量策略，留待 Phase 3 优化）。
/// </summary>
public sealed class CosmosDbSemanticCache : ISemanticCache
{
    private readonly Container _container;
    private readonly SemanticCacheOptions _opts;

    public CosmosDbSemanticCache(CosmosClient client, CosmosDbOptions dbOpts, SemanticCacheOptions cacheOpts)
    {
        _container = client.GetDatabase(dbOpts.DatabaseName).GetContainer(dbOpts.CacheContainerName);
        _opts = cacheOpts;
    }

    public async Task<string?> GetAsync(float[] questionEmbedding, CancellationToken ct = default)
    {
        if (!_opts.Enabled) return null;

        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sql = "SELECT * FROM c WHERE c.expiresAt > @now";
        var query = new QueryDefinition(sql).WithParameter("@now", nowEpoch);

        var feed = _container.GetItemQueryIterator<CosmosSemanticCacheDocument>(query);
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var doc in page)
            {
                if (CosineSimilarity(questionEmbedding, doc.Embedding) >= _opts.SimilarityThreshold)
                    return doc.Answer;
            }
        }

        return null;
    }

    public async Task SetAsync(float[] questionEmbedding, string answer, CancellationToken ct = default)
    {
        if (!_opts.Enabled) return;

        var now = DateTimeOffset.UtcNow;
        var doc = new CosmosSemanticCacheDocument
        {
            Id        = Guid.NewGuid().ToString(),
            Embedding = questionEmbedding,
            Answer    = answer,
            CreatedAt = now.ToUnixTimeSeconds(),
            ExpiresAt = now.AddSeconds(_opts.TtlSeconds).ToUnixTimeSeconds()
        };

        await _container.UpsertItemAsync(doc, new PartitionKey(doc.Id), cancellationToken: ct);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        var query = new QueryDefinition("SELECT c.id FROM c");
        var feed  = _container.GetItemQueryIterator<IdDocument>(query);

        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var item in page)
                await _container.DeleteItemAsync<CosmosSemanticCacheDocument>(
                    item.Id, new PartitionKey(item.Id), cancellationToken: ct);
        }
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.expiresAt > @now")
            .WithParameter("@now", nowEpoch);
        var feed = _container.GetItemQueryIterator<int>(query);
        if (!feed.HasMoreResults) return 0;
        var page = await feed.ReadNextAsync(ct);
        return page.FirstOrDefault();
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < float.Epsilon ? 0f : dot / denom;
    }

    private sealed class CosmosSemanticCacheDocument
    {
        [JsonPropertyName("id")]        public string  Id        { get; set; } = string.Empty;
        [JsonPropertyName("embedding")] public float[] Embedding { get; set; } = [];
        [JsonPropertyName("answer")]    public string  Answer    { get; set; } = string.Empty;
        [JsonPropertyName("createdAt")] public long    CreatedAt { get; set; }
        [JsonPropertyName("expiresAt")] public long    ExpiresAt { get; set; }
    }

    private sealed class IdDocument
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    }
}
