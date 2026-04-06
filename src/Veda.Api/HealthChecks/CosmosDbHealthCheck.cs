using Veda.Core.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net;
using Veda.Storage;

namespace Veda.Api.HealthChecks;

/// <summary>
/// 验证 CosmosDB 连接是否正常（仅在 StorageProvider=CosmosDb 时注册）。
/// 执行轻量操作：读取数据库属性，不写入任何数据。
/// 404 → Degraded（数据库尚未初始化，连接本身正常）
/// 其他异常 → Unhealthy（真正的连接问题）
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
