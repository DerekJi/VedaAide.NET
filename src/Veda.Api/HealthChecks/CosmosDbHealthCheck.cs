using Veda.Core.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net;
using Veda.Storage;

namespace Veda.Api.HealthChecks;

/// <summary>
/// Verifies that the CosmosDB connection is healthy (only registered when StorageProvider=CosmosDb).
/// Performs a lightweight operation: reads database properties without writing any data.
/// 404 → Degraded (database not yet initialized; connectivity itself is fine)
/// Other exceptions → Unhealthy (genuine connection problem)
/// </summary>
public sealed class CosmosDbHealthCheck(CosmosClient client, CosmosDbOptions options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = client.GetDatabase(options.DatabaseName);
            await db.ReadAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy("CosmosDB reachable");
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Database not yet created — connectivity is fine, initializer hasn't run yet
            return HealthCheckResult.Degraded(
                $"CosmosDB reachable but database '{options.DatabaseName}' not found (not yet initialized)");
        }
        catch (CosmosException ex)
        {
            return HealthCheckResult.Unhealthy($"CosmosDB error: {ex.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("CosmosDB unreachable", ex);
        }
    }
}
