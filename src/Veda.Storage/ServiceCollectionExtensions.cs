using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

            var cosmosClient = string.IsNullOrWhiteSpace(accountKey)
                ? new CosmosClient(endpoint, new DefaultAzureCredential())
                : new CosmosClient(endpoint, accountKey);

            // Build CosmosDbOptions from config manually (avoids IConfiguration binding extensions dependency)
            var cosmosOpts = new CosmosDbOptions
            {
                Endpoint              = endpoint,
                AccountKey            = accountKey,
                DatabaseName          = cfg["Veda:CosmosDb:DatabaseName"]          ?? "VedaAide",
                ChunksContainerName   = cfg["Veda:CosmosDb:ChunksContainerName"]   ?? "VectorChunks",
                EmbeddingDimensions   = int.TryParse(cfg["Veda:CosmosDb:EmbeddingDimensions"], out var dims) ? dims : 1024
            };

            services.AddSingleton(cosmosClient);
            services.AddSingleton(cosmosOpts);
            services.AddScoped<IVectorStore, CosmosDbVectorStore>();
            services.AddSingleton<CosmosDbInitializer>();
        }
        else
        {
            services.AddScoped<IVectorStore, SqliteVectorStore>();
        }

        return services;
    }
}
