using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

// Alias to avoid conflict with Microsoft.Extensions.AI.Embedding<T>
using CosmosEmbedding = Microsoft.Azure.Cosmos.Embedding;
using System.Collections.ObjectModel;

namespace Veda.Storage;

/// <summary>
/// 应用启动时确保 CosmosDB 数据库和向量容器存在且配置正确。
/// 容器使用 DiskANN 向量索引（余弦距离），Partition Key = /documentId。
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
            "CosmosDbInitializer: ensuring database '{Db}' and container '{Container}' exist",
            opts.DatabaseName, opts.ChunksContainerName);

        // Create database (throughput = null → Serverless)
        var dbResponse = await client.CreateDatabaseIfNotExistsAsync(
            opts.DatabaseName, throughput: null, cancellationToken: ct);
        var db = dbResponse.Database;

        var containerProps = new ContainerProperties
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

        // Add embedding path to ExcludedPaths after construction (ExcludedPaths collection is read-only at init)
        containerProps.IndexingPolicy.ExcludedPaths.Add(new ExcludedPath { Path = "/embedding/*" });

        await db.CreateContainerIfNotExistsAsync(containerProps, cancellationToken: ct);

        logger.LogInformation("CosmosDbInitializer: ready");
    }
}
