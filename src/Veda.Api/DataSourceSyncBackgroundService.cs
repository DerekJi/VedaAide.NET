using Veda.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Veda.Core.Interfaces;
using Veda.Services.DataSources;


namespace Veda.Api;

/// <summary>
/// Background service: automatically triggers all enabled <see cref="IDataSourceConnector"/> connectors to sync at the configured interval.
/// Configuration section: <c>Veda:DataSources:AutoSync</c> (Enabled + IntervalMinutes).
/// Uses IServiceScopeFactory to create a Scoped scope so each sync cycle gets independent DbContext / Service instances.
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
