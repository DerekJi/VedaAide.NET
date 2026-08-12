using Veda.Core.Options;
using Microsoft.Extensions.Options;
using Veda.Services;

namespace Veda.Api.Middleware;

/// <summary>
/// API Key authentication middleware.
/// Reads the key from the X-Api-Key request header and compares it with Veda:Security:ApiKey.
/// /api/admin/* and /mcp endpoints are authenticated with Veda:Security:AdminApiKey.
/// Skips validation when no key is configured (convenient for local development).
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<VedaOptions> options)
{
    private const string ApiKeyHeader = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip auth for Swagger UI, GraphQL Banana UI, health checks, MCP negotiation
        if (IsExcluded(path))
        {
            await next(context);
            return;
        }

        var requestKey = context.Request.Headers[ApiKeyHeader].FirstOrDefault();
        var security   = options.Value.Security;

        if (path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/mcp",       StringComparison.OrdinalIgnoreCase))
        {
            var adminKey = security.AdminApiKey;
            if (!string.IsNullOrWhiteSpace(adminKey) && requestKey != adminKey)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing Admin API key." });
                return;
            }
        }
        else
        {
            var apiKey = security.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey) && requestKey != apiKey)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key." });
                return;
            }
        }

        await next(context);
    }

    private static bool IsExcluded(string path) =>
        path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/graphql", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/health", StringComparison.OrdinalIgnoreCase)      ||
        path.StartsWith("/api/public", StringComparison.OrdinalIgnoreCase);
}
