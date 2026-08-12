using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Veda.MCP.Tools;

namespace Veda.MCP;

public static class McpServiceExtensions
{
    /// <summary>
    /// Registers the VedaAide MCP Server (HTTP/SSE transport) and mounts the knowledge base read-only tools.
    /// IngestTools has been removed: the /mcp channel is positioned as a public read-only knowledge base interface.
    /// </summary>
    public static IServiceCollection AddVedaMcp(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<KnowledgeBaseTools>();

        return services;
    }

    /// <summary>
    /// Mounts the MCP SSE endpoint at /mcp.
    /// </summary>
    public static IEndpointRouteBuilder MapVedaMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp");
        return endpoints;
    }
}
