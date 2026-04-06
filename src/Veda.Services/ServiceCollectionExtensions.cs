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
    /// 注册 AI 服务（Embedding + Chat LLM）。
    /// 通过 Veda:EmbeddingProvider / Veda:LlmProvider 配置项选择提供商：
    /// "Ollama"（默认，本地）或 "AzureOpenAI"（云端）。
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

        // DIP 适配器：将 SK 的 IChatCompletionService 包装为领域接口 IChatService
        // optional ITokenUsageRepository + ICurrentUserService 由 DI 自动注入
        services.AddScoped<IChatService>(sp =>
            new OllamaChatService(
                sp.GetRequiredService<IChatCompletionService>(),
                sp.GetService<ITokenUsageRepository>(),
                sp.GetService<ICurrentUserService>()));
        // LLM Router: 根据 QueryMode 分发到 simple / advanced 服务
        services.AddScoped<ILlmRouter, LlmRouterService>();

        // Phase 2: 防幻觉服务
        services.AddScoped<IHallucinationGuardService, HallucinationGuardService>();

        // RAG 查询的共用辅助服务
        services.AddScoped<IRagQueryHelper, RagQueryHelper>();

        // ISP 拆分的具体服务
        services.AddScoped<IDocumentIngestor, DocumentIngestService>();
        services.AddScoped<IQueryStreamService, QueryStreamService>();
        services.AddScoped<IQueryService, QueryService>();
        services.AddScoped<IPublicResumeTailoringService, PublicResumeTailoringService>();

        // 多模态文件提取器（文件上传管线）
        services.AddSingleton<AzureDiQuotaState>();  // 跨请求持久化配额超限状态
        services.AddScoped<DocumentIntelligenceFileExtractor>();
        services.AddScoped<VisionModelFileExtractor>();
        services.AddScoped<PdfTextLayerExtractor>();
        services.AddScoped<EphemeralContextExtractor>();

        // 混合检索（双通道 RRF 融合）
        services.AddScoped<IHybridRetriever, HybridRetriever>();

        // 语义增强层（查询扩展 + 别名标签）
        // 有词库文件配置时注入 PersonalVocabularyEnhancer，否则透传 NoOp
        services.AddScoped<ISemanticEnhancer>(sp =>
        {
            var semanticOpts = sp.GetRequiredService<IOptions<SemanticsOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(semanticOpts.VocabularyFilePath) && File.Exists(semanticOpts.VocabularyFilePath))
                return new PersonalVocabularyEnhancer(semanticOpts);
            return new NoOpSemanticEnhancer();
        });

        // 文档版本对比服务
        services.AddScoped<IDocumentDiffService, DocumentDiffService>();

        // Sprint 4: 反馈 boost service（不依赖 DB，仅包装 IUserMemoryStore）
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

