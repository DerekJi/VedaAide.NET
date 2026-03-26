using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Veda.Api.HealthChecks;

/// <summary>
/// 验证 Azure OpenAI 端点配置是否存在（不调用 API，避免消耗 token）。
/// 真正的连通性在第一次 query 时验证。
/// </summary>
public sealed class AzureOpenAIConfigHealthCheck(IConfiguration cfg) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var endpoint = cfg["Veda:AzureOpenAI:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return Task.FromResult(HealthCheckResult.Degraded(
                "Veda:AzureOpenAI:Endpoint not configured"));

        return Task.FromResult(HealthCheckResult.Healthy($"Endpoint configured: {endpoint}"));
    }
}
