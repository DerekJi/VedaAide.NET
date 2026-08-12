using Veda.Core.Options;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using System.ClientModel;

namespace Veda.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Keyed DI key for the vision <see cref="IChatClient"/> instance.</summary>
    public const string VisionServiceKey = "vision";
    
    /// <summary>
    /// Registers the AI services (Embedding + Chat LLM).
    /// The provider is selected via the Veda:EmbeddingProvider / Veda:LlmProvider settings:
    /// "Ollama" (default, local) or "AzureOpenAI" (cloud).
    /// </summary>
    public static IServiceCollection AddVedaAiServices(
        this IServiceCollection services, IConfiguration cfg)
    {
        var opts          = cfg.GetSection("Veda").Get<VedaOptions>() ?? new VedaOptions();
        var visionOpts    = opts.Vision;
        
        // ── Embeddings (Microsoft.Extensions.AI) ───────────────────────────
        if (opts.EmbeddingProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = opts.AzureOpenAI.Endpoint
                ?? throw new InvalidOperationException("Veda:AzureOpenAI:Endpoint is required");
            var azureEmbedClient = string.IsNullOrWhiteSpace(opts.AzureOpenAI.ApiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(opts.AzureOpenAI.ApiKey!));
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
                azureEmbedClient.GetEmbeddingClient(opts.AzureOpenAI.EmbeddingDeployment).AsIEmbeddingGenerator());
        }
        else
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
                CreateOllamaOpenAIClient(opts.OllamaEndpoint)
                    .GetEmbeddingClient(opts.EmbeddingModel)
                    .AsIEmbeddingGenerator());
        }

        // ── Chat LLM (Microsoft.Extensions.AI) ───────────────────────────
        // Use IChatClient from Microsoft.Extensions.AI for chat completions
        if (opts.LlmProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = opts.AzureOpenAI.Endpoint
                ?? throw new InvalidOperationException("Veda:AzureOpenAI:Endpoint is required");
            var azureChatClient = string.IsNullOrWhiteSpace(opts.AzureOpenAI.ApiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(opts.AzureOpenAI.ApiKey!));
            // Wrap AzureOpenAIClient as IChatClient using the extension method
            var chatClient = azureChatClient.GetChatClient(opts.AzureOpenAI.ChatDeployment).AsIChatClient();
            services.AddSingleton<IChatClient>(chatClient);
        }
        else
        {
            // Ollama chat client via OpenAI-compatible endpoint
            services.AddSingleton<IChatClient>(sp => CreateOllamaChatClient(opts.ChatModel, opts.OllamaEndpoint));
        }

        // ── Vision service registration ───────────────────────────────────
        RegisterVisionService(services, opts, visionOpts);

        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<IDocumentProcessor, TextDocumentProcessor>();

        // DIP adapter: wraps IChatClient as the domain interface IChatService
        services.AddScoped<IChatService>(sp =>
            new AiChatService(
                sp.GetRequiredService<IChatClient>(),
                sp.GetService<ITokenUsageRepository>(),
                sp.GetService<ICurrentUserService>()));
        
        // LLM Router: dispatches to simple / advanced services based on QueryMode
        services.AddScoped<ILlmRouter, LlmRouterService>();

        // Phase 2: hallucination prevention service
        services.AddScoped<IHallucinationGuardService, HallucinationGuardService>();

        // Shared helper service for RAG queries
        services.AddScoped<IRagQueryHelper, RagQueryHelper>();

        // Concrete services split by ISP
        services.AddScoped<IDocumentIngestor, DocumentIngestService>();
        services.AddScoped<IQueryStreamService, QueryStreamService>();
        services.AddScoped<IQueryService, QueryService>();
        services.AddScoped<IPublicResumeTailoringService, PublicResumeTailoringService>();

        // Multimodal file extractors (file upload pipeline)
        services.AddSingleton<AzureDiQuotaState>();  // Persists quota-exceeded state across requests
        services.AddScoped<DocumentIntelligenceFileExtractor>();
        services.AddScoped<VisionModelFileExtractor>();
        services.AddScoped<PdfTextLayerExtractor>();
        services.AddScoped<EphemeralContextExtractor>();

        // Hybrid retrieval (dual-channel RRF fusion)
        services.AddScoped<IHybridRetriever, HybridRetriever>();

        // Semantic enhancement layer (query expansion + alias tags)
        // Injects PersonalVocabularyEnhancer when a vocabulary file is configured, otherwise passes through NoOp
        services.AddScoped<ISemanticEnhancer>(sp =>
        {
            var semanticOpts = sp.GetRequiredService<IOptions<SemanticsOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(semanticOpts.VocabularyFilePath) && File.Exists(semanticOpts.VocabularyFilePath))
                return new PersonalVocabularyEnhancer(semanticOpts);
            return new NoOpSemanticEnhancer();
        });

        // Document version diff service
        services.AddScoped<IDocumentDiffService, DocumentDiffService>();

        // Sprint 4: feedback boost service (no DB dependency, just wraps IUserMemoryStore)
        services.AddScoped<IFeedbackBoostService, FeedbackBoostService>();

        return services;
    }
    
    /// <summary>Creates an Ollama-compatible IChatClient via Ollama's OpenAI-compatible endpoint.</summary>
    private static IChatClient CreateOllamaChatClient(string model, string endpoint, int? timeoutSeconds = null)
        => CreateOllamaOpenAIClient(endpoint, timeoutSeconds).GetChatClient(model).AsIChatClient();

    /// <summary>
    /// Creates an OpenAI SDK client pointing at Ollama's OpenAI-compatible API
    /// (the endpoint is normalized to include the /v1 path).
    /// </summary>
    private static OpenAIClient CreateOllamaOpenAIClient(string endpoint, int? timeoutSeconds = null)
    {
        var trimmed = endpoint.TrimEnd('/');
        var baseUrl = trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/v1";
        var options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
        if (timeoutSeconds is not null)
            options.NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds.Value);
        return new OpenAIClient(
            new ApiKeyCredential("ollama"), // Ollama ignores the key; any non-empty value is accepted
            options);
    }

    /// <summary>
    /// Registers the "vision" service.
    /// Priority rule (no explicit provider field needed):
    ///   1. <c>Veda:Vision:OllamaModel</c> non-empty  → dedicated Ollama VL model
    ///   2. <c>Veda:AzureOpenAI:Endpoint</c> non-empty → AzureOpenAI using <c>Vision:ChatDeployment</c>
    ///   3. Neither configured                         → reuse the main chat service
    /// This keeps Vision independent of <c>LlmProvider</c> without requiring an extra ModelProvider field.
    /// </summary>
    private static void RegisterVisionService(
        IServiceCollection services, VedaOptions opts, VisionOptions visionOpts)
    {
        if (!string.IsNullOrWhiteSpace(visionOpts.OllamaModel))
        {
            // Dedicated Ollama VL model (e.g. qwen3-vl:8b).
            // Use a longer NetworkTimeout: VL models under VRAM pressure can exceed the default 100 s timeout.
            services.AddKeyedSingleton<IChatClient>(VisionServiceKey, 
                (sp, _) => CreateOllamaChatClient(visionOpts.OllamaModel, opts.OllamaEndpoint, visionOpts.TimeoutSeconds));
        }
        else if (!string.IsNullOrWhiteSpace(opts.AzureOpenAI.Endpoint))
        {
            // AzureOpenAI vision (works regardless of main LlmProvider).
            services.AddKeyedSingleton<IChatClient>(VisionServiceKey, (sp, _) =>
            {
                var visionAzureClient = string.IsNullOrWhiteSpace(opts.AzureOpenAI.ApiKey)
                    ? new AzureOpenAIClient(new Uri(opts.AzureOpenAI.Endpoint), new DefaultAzureCredential())
                    : new AzureOpenAIClient(new Uri(opts.AzureOpenAI.Endpoint), new AzureKeyCredential(opts.AzureOpenAI.ApiKey!));
                return visionAzureClient.GetChatClient(visionOpts.ChatDeployment).AsIChatClient();
            });
        }
        else
        {
            // No vision-specific model configured — reuse main chat service.
            services.AddKeyedSingleton<IChatClient>(VisionServiceKey,
                (sp, _) => sp.GetRequiredService<IChatClient>());
        }
    }
}

