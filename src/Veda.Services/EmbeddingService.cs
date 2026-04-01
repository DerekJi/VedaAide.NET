using Microsoft.Extensions.AI;
using Veda.Core.Interfaces;

namespace Veda.Services;

/// <summary>
/// 基于 Microsoft.Extensions.AI IEmbeddingGenerator 的 Embedding 服务。
/// 捕获 M.E.AI Usage 并写入 ITokenUsageRepository。
/// </summary>
public sealed class EmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    ITokenUsageRepository? usageRepo = null,
    ICurrentUserService? currentUser = null) : IEmbeddingService
{
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var results = await inner.GenerateAsync([text], cancellationToken: ct);
        RecordUsage(results.Usage, results[0].ModelId ?? "embedding", "Embedding");
        return results[0].Vector.ToArray();
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
    {
        var list = texts.ToList();
        var results = await inner.GenerateAsync(list, cancellationToken: ct);
        RecordUsage(results.Usage, results.FirstOrDefault()?.ModelId ?? "embedding", "Embedding");
        return results.Select(r => r.Vector.ToArray()).ToList();
    }

    private void RecordUsage(UsageDetails? usage, string modelName, string opType)
    {
        if (usageRepo is null || usage is null) return;
        var promptTokens = (int)(usage.InputTokenCount ?? 0);
        if (promptTokens == 0) return;
        var userId = currentUser?.UserId ?? "anonymous";
        _ = usageRepo.RecordAsync(new TokenUsageRecord(userId, modelName, opType, promptTokens, 0));
    }
}
