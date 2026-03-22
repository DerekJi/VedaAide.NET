using Microsoft.Extensions.DependencyInjection;

namespace Veda.Prompts;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVedaPrompts(this IServiceCollection services)
    {
        services.AddSingleton<IContextWindowBuilder, ContextWindowBuilder>();
        services.AddSingleton<IChainOfThoughtStrategy, ChainOfThoughtStrategy>();
        return services;
    }
}
