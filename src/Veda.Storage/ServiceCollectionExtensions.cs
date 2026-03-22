using Microsoft.Extensions.DependencyInjection;

namespace Veda.Storage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVedaStorage(this IServiceCollection services, string dbPath)
    {
        services.AddDbContext<VedaDbContext>(opt =>
            opt.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<IVectorStore, SqliteVectorStore>();
        services.AddScoped<IPromptTemplateRepository, PromptTemplateRepository>();
        services.AddScoped<ISyncStateStore, SyncStateStore>();
        services.AddScoped<IEvalDatasetRepository, EvalDatasetRepository>();
        services.AddScoped<IEvalReportRepository, EvalReportRepository>();
        return services;
    }
}
