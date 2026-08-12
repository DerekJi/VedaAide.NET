using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Veda.Api.HealthChecks;

/// <summary>
/// Verifies that the Azure OpenAI endpoint configuration is present (without calling the API, to avoid consuming tokens).
/// Real connectivity is validated on the first query.
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
