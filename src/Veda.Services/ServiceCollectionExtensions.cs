using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Veda.Services;

public static class ServiceCollectionExtensions
{
    /// <param name="ollamaEndpoint">Ollama base URL，例如 http://localhost:11434</param>
    /// <param name="embeddingModel">Embedding 模型名，例如 nomic-embed-text</param>
    /// <param name="chatModel">Chat 模型名，例如 qwen3:8b</param>
    public static IServiceCollection AddVedaAiServices(
        this IServiceCollection services,
        string ollamaEndpoint,
        string embeddingModel,
        string chatModel)
    {
        var kernelBuilder = services.AddKernel();
        kernelBuilder.AddOllamaEmbeddingGenerator(embeddingModel, new Uri(ollamaEndpoint));
        kernelBuilder.AddOllamaChatCompletion(chatModel, new Uri(ollamaEndpoint));

        services.AddScoped<IEmbeddingService, EmbeddingService>();
        services.AddScoped<IDocumentProcessor, TextDocumentProcessor>();

        // DIP 适配器：将 SK 的 IChatCompletionService 包装为领域接口 IChatService
        services.AddScoped<IChatService, OllamaChatService>();

        // Phase 2: 防幻觉服务
        services.AddScoped<IHallucinationGuardService, HallucinationGuardService>();

        // ISP 拆分的具体服务
        services.AddScoped<IDocumentIngestor, DocumentIngestService>();
        services.AddScoped<IQueryService, QueryService>();

        return services;
    }
}
