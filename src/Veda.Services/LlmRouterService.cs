using Veda.Core.Options;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Services;

/// <summary>
/// LLM router implementation.
/// Simple mode → the DI-injected default IChatService (Ollama or Azure OpenAI GPT-4o-mini).
/// Advanced mode → DeepSeek (via the SK OpenAI-compatible connector + OllamaChatService adapter).
/// When the DeepSeek ApiKey is not configured, Advanced automatically falls back to Simple.
/// </summary>
public sealed class LlmRouterService : ILlmRouter
{
    private readonly IChatService _simpleService;
    private readonly Lazy<IChatService> _advancedService;

    public LlmRouterService(IChatService simpleService, IOptions<VedaOptions> options)
    {
        _simpleService = simpleService;
        var ds = options.Value.DeepSeek;

        _advancedService = new Lazy<IChatService>(() =>
        {
            if (string.IsNullOrWhiteSpace(ds.ApiKey))
                return simpleService; // Graceful fallback

            // Use SK OpenAI connector with DeepSeek-compatible endpoint (all named args to avoid overload ambiguity)
            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelId: ds.ChatModel, apiKey: ds.ApiKey!, endpoint: new Uri(ds.BaseUrl))
                .Build();
            var inner = kernel.GetRequiredService<IChatCompletionService>();
            return new OllamaChatService(inner); // OllamaChatService is a generic IChatCompletionService adapter
        });
    }

    public IChatService Resolve(QueryMode mode)
        => mode == QueryMode.Advanced ? _advancedService.Value : _simpleService;
}
