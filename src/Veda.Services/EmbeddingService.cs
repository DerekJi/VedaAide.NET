using Microsoft.Extensions.AI;

namespace Veda.Services;

/// <summary>
/// 基于 Microsoft.Extensions.AI IEmbeddingGenerator 的 Embedding 服务。
/// 底层实现（Ollama / Azure OpenAI）通过 DI 注入，本类不感知。
/// </summary>
public sealed class EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> inner) : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var results = await inner.GenerateAsync([text], cancellationToken: ct);
        return results[0].Vector.ToArray();
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var list = texts.ToList();
        var results = await inner.GenerateAsync(list, cancellationToken: ct);
        return results.Select(r => r.Vector.ToArray()).ToList();
    }
}
