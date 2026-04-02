using System.Text.Json;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        // Suppress PendingModelChangesWarning: EF Core 10 compares model hash from Designer.cs of the
        // last migration; Sprint3/Sprint4 migrations lack Designer.cs so the hash check produces a
        // false-positive mismatch. The model snapshot is correct and all migrations are present.
        services.AddDbContext<VedaDbContext>(opt =>
            opt.UseSqlite($"Data Source={dbPath}")
               .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<IPromptTemplateRepository, PromptTemplateRepository>();
        services.AddScoped<ISyncStateStore, SyncStateStore>();
        services.AddScoped<IEvalDatasetRepository, EvalDatasetRepository>();
        services.AddScoped<IEvalReportRepository, EvalReportRepository>();

        // Vector store: SQLite or CosmosDB
        var storageProvider = cfg["Veda:StorageProvider"] ?? "Sqlite";
        if (storageProvider.Equals("CosmosDb", StringComparison.OrdinalIgnoreCase))
        {
            var cosmosOpts = cfg.GetSection("Veda:CosmosDb").Get<CosmosDbOptions>() ?? new CosmosDbOptions();
            if (string.IsNullOrWhiteSpace(cosmosOpts.Endpoint))
                throw new InvalidOperationException("Veda:CosmosDb:Endpoint is required when StorageProvider=CosmosDb");

            var cosmosClientOptions = new CosmosClientOptions
            {
                Serializer = new SystemTextJsonCosmosSerializer(new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })
            };
            var cosmosClient = string.IsNullOrWhiteSpace(cosmosOpts.AccountKey)
                ? new CosmosClient(cosmosOpts.Endpoint, new DefaultAzureCredential(), cosmosClientOptions)
                : new CosmosClient(cosmosOpts.Endpoint, cosmosOpts.AccountKey, cosmosClientOptions);

            var cacheOpts = cfg.GetSection("Veda:SemanticCache").Get<SemanticCacheOptions>() ?? new SemanticCacheOptions();

            services.AddSingleton(cosmosClient);
            services.AddSingleton(cosmosOpts);
            services.AddSingleton(cacheOpts);
            services.AddScoped<IVectorStore, CosmosDbVectorStore>();
            services.AddScoped<ISemanticCache, CosmosDbSemanticCache>();
            services.AddScoped<IUserMemoryStore, CosmosDbUserMemoryStore>();
            services.AddScoped<ITokenUsageRepository, CosmosDbTokenUsageRepository>();
            services.AddSingleton<CosmosDbInitializer>();
        }
        else
        {
            var cacheOpts = cfg.GetSection("Veda:SemanticCache").Get<SemanticCacheOptions>() ?? new SemanticCacheOptions();
            services.AddSingleton(cacheOpts);
            services.AddScoped<IVectorStore, SqliteVectorStore>();
            services.AddScoped<ISemanticCache, SqliteSemanticCache>();
            services.AddScoped<IUserMemoryStore, UserMemoryStore>();
            services.AddScoped<ITokenUsageRepository, TokenUsageRepository>();
        }

        // Knowledge governance (sharing groups, document permissions, consensus candidates) — always SQLite
        services.AddScoped<IKnowledgeGovernanceService, KnowledgeGovernanceService>();

        // Chat session persistence (always SQLite for metadata)
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();

        return services;
    }
}
