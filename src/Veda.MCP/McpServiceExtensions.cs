using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Veda.MCP.Tools;

namespace Veda.MCP;

public static class McpServiceExtensions
{
    /// <summary>
    /// 注册 VedaAide MCP Server（HTTP/SSE 传输），挂载知识库只读工具。
    /// IngestTools 已移除：/mcp 通道定位为公共知识库只读接口。
    /// </summary>
    public static IServiceCollection AddVedaMcp(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<KnowledgeBaseTools>();

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
