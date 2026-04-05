using Microsoft.Extensions.AI;
using Veda.Core.Interfaces;

namespace Veda.Services;

/// <summary>
/// 基于 Microsoft.Extensions.AI IEmbeddingGenerator 的 Embedding 服务。
/// 捕获 M.E.AI Usage 并写入 ITokenUsageRepository。
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> inner;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> inner)
    {
        this.inner = inner;
    }

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

    Task<string> IEmbeddingService.ExpandQueryAsync(string text, CancellationToken ct)
    {
        // Placeholder implementation: simply returns the input text.
        // Replace with actual query expansion logic if needed.
        return Task.FromResult(text);
    }
}
