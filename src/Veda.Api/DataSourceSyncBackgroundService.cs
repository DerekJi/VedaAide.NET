using Veda.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Veda.Core.Interfaces;
using Veda.Services.DataSources;


namespace Veda.Api;

/// <summary>
/// 后台服务：按配置的间隔自动触发所有已启用的 <see cref="IDataSourceConnector"/> 执行同步。
/// 配置节：<c>Veda:DataSources:AutoSync</c>（Enabled + IntervalMinutes）。
/// 通过 IServiceScopeFactory 创建 Scoped 作用域，确保每次同步获取独立的 DbContext / Service 实例。
/// </summary>
public sealed class DataSourceSyncBackgroundService(
    IServiceScopeFactory                    scopeFactory,
    IOptions<DataSourceSyncOptions>         options,
    ILogger<DataSourceSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        if (!opts.Enabled)
        {
            logger.LogInformation("DataSourceSyncBackgroundService: auto-sync is disabled, background service exiting.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, opts.IntervalMinutes));
        logger.LogInformation("DataSourceSyncBackgroundService: starting, interval = {Interval} min", interval.TotalMinutes);

        // Delay first run slightly so API startup completes first
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("DataSourceSyncBackgroundService: running scheduled sync");

            try
            {
                await RunSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DataSourceSyncBackgroundService: unhandled error during sync cycle");
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("DataSourceSyncBackgroundService: stopped.");
    }

    private async Task RunSyncAsync(CancellationToken ct)
    {
        // New scope per sync cycle — Scoped services (IDocumentIngestor, IVectorStore, etc.) are safe
        await using var scope      = scopeFactory.CreateAsyncScope();
        var connectors = scope.ServiceProvider.GetServices<IDataSourceConnector>();

        foreach (var connector in connectors.Where(c => c.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            logger.LogInformation("DataSourceSyncBackgroundService: syncing connector '{Name}'", connector.Name);

            var result = await connector.SyncAsync(ct);

            logger.LogInformation(
                "DataSourceSyncBackgroundService: '{Name}' — {Files} files, {Chunks} chunks, {Errors} errors",
                result.ConnectorName, result.FilesProcessed, result.ChunksStored, result.Errors.Count);

            foreach (var err in result.Errors)
                logger.LogWarning("DataSourceSyncBackgroundService: [{Name}] {Error}", connector.Name, err);
        }
    }
}
