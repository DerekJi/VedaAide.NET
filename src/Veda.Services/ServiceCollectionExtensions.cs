using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Veda.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 AI 服务（Embedding + Chat LLM）。
    /// 通过 Veda:EmbeddingProvider / Veda:LlmProvider 配置项选择提供商：
    /// "Ollama"（默认，本地）或 "AzureOpenAI"（云端）。
    /// </summary>
    public static IServiceCollection AddVedaAiServices(
        this IServiceCollection services, IConfiguration cfg)
    {
        var embeddingProvider = cfg["Veda:EmbeddingProvider"] ?? "Ollama";
        var llmProvider       = cfg["Veda:LlmProvider"]       ?? "Ollama";
        var kernelBuilder     = services.AddKernel();

        // ── Embedding ────────────────────────────────────────────────────────
        if (embeddingProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint   = cfg["Veda:AzureOpenAI:Endpoint"]   ?? throw new InvalidOperationException("Veda:AzureOpenAI:Endpoint is required");
            var apiKey     = cfg["Veda:AzureOpenAI:ApiKey"];
            var deployment = cfg["Veda:AzureOpenAI:EmbeddingDeployment"] ?? "text-embedding-3-small";

            // Build AzureOpenAIClient: separate constructors for apiKey vs Managed Identity
            var azureEmbedClient = string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            kernelBuilder.Services.AddAzureOpenAIEmbeddingGenerator(deployment, azureEmbedClient);
        }
        else
        {
            var ollamaEndpoint = cfg["Veda:OllamaEndpoint"] ?? "http://localhost:11434";
            var embeddingModel = cfg["Veda:EmbeddingModel"] ?? "bge-m3";
            kernelBuilder.AddOllamaEmbeddingGenerator(embeddingModel, new Uri(ollamaEndpoint));
        }

        // ── Chat LLM ─────────────────────────────────────────────────────────
        if (llmProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint   = cfg["Veda:AzureOpenAI:Endpoint"]    ?? throw new InvalidOperationException("Veda:AzureOpenAI:Endpoint is required");
            var apiKey     = cfg["Veda:AzureOpenAI:ApiKey"];
            var deployment = cfg["Veda:AzureOpenAI:ChatDeployment"] ?? "gpt-4o-mini";

            var azureChatClient = string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            kernelBuilder.AddAzureOpenAIChatCompletion(deployment, azureChatClient);
        }
        else
        {
            var ollamaEndpoint = cfg["Veda:OllamaEndpoint"] ?? "http://localhost:11434";
            var chatModel      = cfg["Veda:ChatModel"]      ?? "qwen3:8b";
            kernelBuilder.AddOllamaChatCompletion(chatModel, new Uri(ollamaEndpoint));
        }

        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<IDocumentProcessor, TextDocumentProcessor>();

        // DIP 适配器：将 SK 的 IChatCompletionService 包装为领域接口 IChatService
        services.AddScoped<IChatService, OllamaChatService>();

        // LLM Router: 根据 QueryMode 分发到 simple / advanced 服务
        services.AddScoped<ILlmRouter, LlmRouterService>();

        // Phase 2: 防幻觉服务
        services.AddScoped<IHallucinationGuardService, HallucinationGuardService>();

        // ISP 拆分的具体服务
        services.AddScoped<IDocumentIngestor, DocumentIngestService>();
        services.AddScoped<IQueryService, QueryService>();

        // 多模态文件提取器（文件上传管线）
        services.AddSingleton<AzureDiQuotaState>();  // 跨请求持久化配额超限状态
        services.AddScoped<DocumentIntelligenceFileExtractor>();
        services.AddScoped<VisionModelFileExtractor>();
        services.AddScoped<PdfTextLayerExtractor>();

        // 混合检索（双通道 RRF 融合）
        services.AddScoped<IHybridRetriever, HybridRetriever>();

        // 语义增强层（查询扩展 + 别名标签）
        // 有词库文件配置时注入 PersonalVocabularyEnhancer，否则透传 NoOp
        services.AddScoped<ISemanticEnhancer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SemanticsOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.VocabularyFilePath) && File.Exists(opts.VocabularyFilePath))
                return new PersonalVocabularyEnhancer(opts);
            return new NoOpSemanticEnhancer();
        });

        // 文档版本对比服务
        services.AddScoped<IDocumentDiffService, DocumentDiffService>();

        // Sprint 4: 反馈 boost service（不依赖 DB，仅包装 IUserMemoryStore）
        services.AddScoped<IFeedbackBoostService, FeedbackBoostService>();

        return services;
    }
}
