using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Services;

/// <summary>
/// LLM 路由器实现。
/// Simple 模式 → DI 注入的默认 IChatService（Ollama 或 Azure OpenAI GPT-4o-mini）。
/// Advanced 模式 → DeepSeek（通过 SK OpenAI 兼容连接器 + OllamaChatService 适配器）。
/// 当 DeepSeek ApiKey 未配置时，Advanced 自动降级到 Simple。
/// </summary>
public sealed class LlmRouterService : ILlmRouter
{
    private readonly IChatService _simpleService;
    private readonly Lazy<IChatService> _advancedService;

    public LlmRouterService(IChatService simpleService, IConfiguration cfg)
    {
        _simpleService = simpleService;

        var baseUrl = cfg["Veda:DeepSeek:BaseUrl"]    ?? "https://api.deepseek.com/v1";
        var apiKey  = cfg["Veda:DeepSeek:ApiKey"]     ?? string.Empty;
        var model   = cfg["Veda:DeepSeek:ChatModel"]  ?? "deepseek-chat";

        _advancedService = new Lazy<IChatService>(() =>
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return simpleService; // Graceful fallback

            // Use SK OpenAI connector with DeepSeek-compatible endpoint (all named args to avoid overload ambiguity)
            var kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(modelId: model, apiKey: apiKey, endpoint: new Uri(baseUrl))
                .Build();
            var inner = kernel.GetRequiredService<IChatCompletionService>();
            return new OllamaChatService(inner); // OllamaChatService is a generic IChatCompletionService adapter
        });
    }

    public IChatService Resolve(QueryMode mode)
        => mode == QueryMode.Advanced ? _advancedService.Value : _simpleService;
}
