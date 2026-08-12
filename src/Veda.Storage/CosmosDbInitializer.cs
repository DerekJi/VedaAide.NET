using Veda.Core.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

// Alias to avoid conflict with Microsoft.Extensions.AI.Embedding<T>
using CosmosEmbedding = Microsoft.Azure.Cosmos.Embedding;
using System.Collections.ObjectModel;

namespace Veda.Storage;

/// <summary>
/// Ensures the CosmosDB database and all containers exist and are configured correctly at application startup.
/// VectorChunks: DiskANN vector index (cosine distance), Partition Key = /documentId.
/// SemanticCache: simple container, Partition Key = /id, auto-expired by TTL.
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

        // ── VectorChunks container (DiskANN vector index)──────────────────────
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

        // ── SemanticCache container (TTL auto-expiry, no vector index)──────────────
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
