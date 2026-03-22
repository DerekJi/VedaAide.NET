using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Veda.MCP.Tools;

namespace Veda.MCP;

public static class McpServiceExtensions
{
    /// <summary>
    /// 注册 VedaAide MCP Server（HTTP/SSE 传输），挂载三个知识库工具。
    /// </summary>
    public static IServiceCollection AddVedaMcp(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<KnowledgeBaseTools>()
            .WithTools<IngestTools>();

        return services;
    }

    /// <summary>
    /// 将 MCP SSE 端点挂载到 /mcp。
    /// </summary>
    public static IEndpointRouteBuilder MapVedaMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp");
        return endpoints;
    }
}
