using Microsoft.Extensions.DependencyInjection;
using Veda.Agents.Orchestration;

namespace Veda.Agents;

public static class AgentServiceExtensions
{
    public static IServiceCollection AddVedaAgents(this IServiceCollection services)
    {
        services.AddScoped<IOrchestrationService, OrchestrationService>();
        return services;
    }
}
