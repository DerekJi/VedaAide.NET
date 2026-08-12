using Veda.Core.Options;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System.ClientModel;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Services;

/// <summary>
/// LLM router implementation.
/// Simple mode → the DI-injected default IChatClient (Ollama or Azure OpenAI GPT-4o-mini).
/// Advanced mode → DeepSeek (via OpenAI-compatible endpoint).
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

            // Create OpenAI-compatible client for DeepSeek
            var deepseekClient = new OpenAIClient(
                new ApiKeyCredential(ds.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(ds.BaseUrl) });
            
            var chatClient = deepseekClient.GetChatClient(ds.ChatModel).AsIChatClient();
            return new AiChatService(chatClient); // AiChatService is the MAF-compatible adapter
        });
    }

    public IChatService Resolve(QueryMode mode)
        => mode == QueryMode.Advanced ? _advancedService.Value : _simpleService;
}
