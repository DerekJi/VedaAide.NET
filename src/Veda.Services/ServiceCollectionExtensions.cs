using Veda.Core.Options;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Veda.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>Keyed DI key for the vision <see cref="IChatCompletionService"/> instance.</summary>
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
        var kernelBuilder = services.AddKernel();

        // ── Embedding ────────────────────────────────────────────────────────
        if (opts.EmbeddingProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = opts.AzureOpenAI.Endpoint
                ?? throw new InvalidOperationException("Veda:AzureOpenAI:Endpoint is required");

            // Build AzureOpenAIClient: separate constructors for apiKey vs Managed Identity
            var azureEmbedClient = string.IsNullOrWhiteSpace(opts.AzureOpenAI.ApiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(opts.AzureOpenAI.ApiKey!));
            kernelBuilder.Services.AddAzureOpenAIEmbeddingGenerator(opts.AzureOpenAI.EmbeddingDeployment, azureEmbedClient);
        }
        else
        {
            kernelBuilder.AddOllamaEmbeddingGenerator(opts.EmbeddingModel, new Uri(opts.OllamaEndpoint));
        }

        // ── Chat LLM ─────────────────────────────────────────────────────────
        if (opts.LlmProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = opts.AzureOpenAI.Endpoint
                ?? throw new InvalidOperationException("Veda:AzureOpenAI:Endpoint is required");

            var azureChatClient = string.IsNullOrWhiteSpace(opts.AzureOpenAI.ApiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(opts.AzureOpenAI.ApiKey!));
            kernelBuilder.AddAzureOpenAIChatCompletion(opts.AzureOpenAI.ChatDeployment, azureChatClient);
        }
        else
        {
            kernelBuilder.AddOllamaChatCompletion(opts.ChatModel, new Uri(opts.OllamaEndpoint));
        }

        // ── Vision service registration (unified, independent of main LlmProvider) ──
        // Rule: OllamaModel set → Ollama VL; else AzureOpenAI:Endpoint set → AzureOpenAI; else → fallback to main chat.
        RegisterVisionService(services, opts, visionOpts);

        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<IDocumentProcessor, TextDocumentProcessor>();

        // DIP adapter: wraps SK's IChatCompletionService as the domain interface IChatService
        // optional ITokenUsageRepository + ICurrentUserService are injected automatically by DI
        services.AddScoped<IChatService>(sp =>
            new OllamaChatService(
                sp.GetRequiredService<IChatCompletionService>(),
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

    /// <summary>
    /// Registers the keyed "vision" <see cref="IChatCompletionService"/>.
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
            // Use a longer-timeout HttpClient: VL models under VRAM pressure can exceed
            // the default 100 s HttpClient.Timeout when running in CPU/GPU split mode.
            var visionHttpClient = new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri(opts.OllamaEndpoint.TrimEnd('/') + "/"),
                Timeout     = TimeSpan.FromSeconds(visionOpts.TimeoutSeconds)
            };
            var visionKernel = Kernel.CreateBuilder()
                .AddOllamaChatCompletion(visionOpts.OllamaModel, visionHttpClient)
                .Build();
            services.AddKeyedSingleton<IChatCompletionService>(VisionServiceKey,
                visionKernel.GetRequiredService<IChatCompletionService>());
        }
        else if (!string.IsNullOrWhiteSpace(opts.AzureOpenAI.Endpoint))
        {
            // AzureOpenAI vision (works regardless of main LlmProvider).
            var visionAzureClient = string.IsNullOrWhiteSpace(opts.AzureOpenAI.ApiKey)
                ? new AzureOpenAIClient(new Uri(opts.AzureOpenAI.Endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(opts.AzureOpenAI.Endpoint), new AzureKeyCredential(opts.AzureOpenAI.ApiKey!));
            var visionKernel = Kernel.CreateBuilder()
                .AddAzureOpenAIChatCompletion(visionOpts.ChatDeployment, visionAzureClient)
                .Build();
            services.AddKeyedSingleton<IChatCompletionService>(VisionServiceKey,
                visionKernel.GetRequiredService<IChatCompletionService>());
        }
        else
        {
            // No vision-specific model configured — reuse main chat service.
            services.AddKeyedTransient<IChatCompletionService>(VisionServiceKey,
                (sp, _) => sp.GetRequiredService<IChatCompletionService>());
        }
    }
}

