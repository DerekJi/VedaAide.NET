using System.Text.Json;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Storage;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册存储服务。通过 Veda:StorageProvider 配置项选择后端：
    /// "Sqlite"（默认，本地开发）或 "CosmosDb"（云端部署）。
    /// 元数据仓储（PromptTemplate、SyncState、Eval）始终使用 SQLite。
    /// </summary>
    public static IServiceCollection AddVedaStorage(this IServiceCollection services, IConfiguration cfg)
    {
        var dbPath = cfg["Veda:DbPath"] ?? "veda.db";

        // Metadata repositories always use SQLite
        services.AddDbContext<VedaDbContext>(opt =>
            opt.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IPromptTemplateRepository, PromptTemplateRepository>();
        services.AddScoped<ISyncStateStore, SyncStateStore>();
        services.AddScoped<IEvalDatasetRepository, EvalDatasetRepository>();
        services.AddScoped<IEvalReportRepository, EvalReportRepository>();

        // Vector store: SQLite or CosmosDB
        var storageProvider = cfg["Veda:StorageProvider"] ?? "Sqlite";
        if (storageProvider.Equals("CosmosDb", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = cfg["Veda:CosmosDb:Endpoint"]
                ?? throw new InvalidOperationException("Veda:CosmosDb:Endpoint is required when StorageProvider=CosmosDb");
            var accountKey = cfg["Veda:CosmosDb:AccountKey"];

            var cosmosClientOptions = new CosmosClientOptions
            {
                Serializer = new SystemTextJsonCosmosSerializer(new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            };
            var cosmosClient = string.IsNullOrWhiteSpace(accountKey)
                ? new CosmosClient(endpoint, new DefaultAzureCredential(), cosmosClientOptions)
                : new CosmosClient(endpoint, accountKey, cosmosClientOptions);

            // Build CosmosDbOptions from config manually (avoids IConfiguration binding extensions dependency)
            var cosmosOpts = new CosmosDbOptions
            {
                Endpoint              = endpoint,
                AccountKey            = accountKey,
                DatabaseName          = cfg["Veda:CosmosDb:DatabaseName"]          ?? "VedaAide",
                ChunksContainerName   = cfg["Veda:CosmosDb:ChunksContainerName"]   ?? "VectorChunks",
                CacheContainerName    = cfg["Veda:CosmosDb:CacheContainerName"]    ?? "SemanticCache",
                EmbeddingDimensions   = int.TryParse(cfg["Veda:CosmosDb:EmbeddingDimensions"], out var dims) ? dims : 1024
            };

            var cacheOpts = BuildCacheOptions(cfg);

            services.AddSingleton(cosmosClient);
            services.AddSingleton(cosmosOpts);
            services.AddSingleton(cacheOpts);
            services.AddScoped<IVectorStore, CosmosDbVectorStore>();
            services.AddScoped<ISemanticCache, CosmosDbSemanticCache>();
            services.AddSingleton<CosmosDbInitializer>();
        }
        else
        {
            var cacheOpts = BuildCacheOptions(cfg);
            services.AddSingleton(cacheOpts);
            services.AddScoped<IVectorStore, SqliteVectorStore>();
            services.AddScoped<ISemanticCache, SqliteSemanticCache>();
        }

        return services;
    }

    private static SemanticCacheOptions BuildCacheOptions(IConfiguration cfg) => new()
    {
        Enabled             = bool.TryParse(cfg["Veda:SemanticCache:Enabled"], out var en) && en,
        SimilarityThreshold = float.TryParse(cfg["Veda:SemanticCache:SimilarityThreshold"],
                                  System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  out var thr) ? thr : 0.95f,
        TtlSeconds          = int.TryParse(cfg["Veda:SemanticCache:TtlSeconds"], out var ttl) ? ttl : 3600
    };
}
