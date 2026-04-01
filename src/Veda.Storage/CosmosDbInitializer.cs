using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

// Alias to avoid conflict with Microsoft.Extensions.AI.Embedding<T>
using CosmosEmbedding = Microsoft.Azure.Cosmos.Embedding;
using System.Collections.ObjectModel;

namespace Veda.Storage;

/// <summary>
/// 应用启动时确保 CosmosDB 数据库和所有容器存在且配置正确。
/// VectorChunks：DiskANN 向量索引（余弦距离），Partition Key = /documentId。
/// SemanticCache：简单容器，Partition Key = /id，按 TTL 自动过期。
/// </summary>
public sealed class CosmosDbInitializer(
    CosmosClient client,
    CosmosDbOptions options,
    ILogger<CosmosDbInitializer> logger)
{
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        var opts = options;

        logger.LogInformation(
            "CosmosDbInitializer: ensuring database '{Db}' and containers '{Chunks}', '{Cache}', '{Behaviors}', '{Tokens}' exist",
            opts.DatabaseName, opts.ChunksContainerName, opts.CacheContainerName, opts.BehaviorsContainerName, opts.TokenUsagesContainerName);

        // Create database (throughput = null → Serverless)
        var dbResponse = await client.CreateDatabaseIfNotExistsAsync(
            opts.DatabaseName, throughput: null, cancellationToken: ct);
        var db = dbResponse.Database;

        // ── VectorChunks container（DiskANN 向量索引）──────────────────────
        var chunksProps = new ContainerProperties
        {
            Id = opts.ChunksContainerName,
            PartitionKeyPath = "/documentId",
            VectorEmbeddingPolicy = new VectorEmbeddingPolicy(
                new Collection<CosmosEmbedding>
                {
                    new CosmosEmbedding
                    {
                        Path = "/embedding",
                        DataType = VectorDataType.Float32,
                        DistanceFunction = DistanceFunction.Cosine,
                        Dimensions = opts.EmbeddingDimensions
                    }
                }),
            IndexingPolicy = new IndexingPolicy
            {
                VectorIndexes =
                [
                    new VectorIndexPath { Path = "/embedding", Type = VectorIndexType.DiskANN }
                ]
            }
        };
        // Embedding path must be excluded from regular index
        chunksProps.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/embedding/*" });
        await db.CreateContainerIfNotExistsAsync(chunksProps, cancellationToken: ct);

        // ── SemanticCache container（TTL 自动过期，无向量索引）──────────────
        var cacheProps = new ContainerProperties
        {
            Id = opts.CacheContainerName,
            PartitionKeyPath = "/id",
            DefaultTimeToLive = -1   // enable TTL; per-item _ttl controls actual expiry
        };
        await db.CreateContainerIfNotExistsAsync(cacheProps, cancellationToken: ct);

        // ── UserBehaviors container (user feedback events, Partition Key = /userId) ──
        var behaviorsProps = new ContainerProperties
        {
            Id = opts.BehaviorsContainerName,
            PartitionKeyPath = "/userId"
        };
        await db.CreateContainerIfNotExistsAsync(behaviorsProps, cancellationToken: ct);

        // ── TokenUsages container (AI token consumption log, Partition Key = /userId) ──
        var tokenUsagesProps = new ContainerProperties
        {
            Id = opts.TokenUsagesContainerName,
            PartitionKeyPath = "/userId"
        };
        await db.CreateContainerIfNotExistsAsync(tokenUsagesProps, cancellationToken: ct);

        // ── ChatSessions container (Partition Key = /userId, user-isolated) ──
        var chatSessionsProps = new ContainerProperties
        {
            Id = opts.ChatSessionsContainerName,
            PartitionKeyPath = "/userId"
        };
        await db.CreateContainerIfNotExistsAsync(chatSessionsProps, cancellationToken: ct);

        logger.LogInformation("CosmosDbInitializer: ready");
    }
}
