using System.Runtime.InteropServices;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// 基于 SQLite 的语义缓存实现。
/// 将所有缓存条目加载到内存后通过余弦相似度匹配。
/// 适合中小规模场景（本地开发 / 低流量部署）。
/// </summary>
public sealed class SqliteSemanticCache(VedaDbContext db, SemanticCacheOptions opts) : ISemanticCache
{
    public async Task<string?> GetAsync(float[] questionEmbedding, CancellationToken ct = default)
    {
        if (!opts.Enabled) return null;

        var nowTicks = DateTimeOffset.UtcNow.Ticks;

        // Load only non-expired entries to keep memory footprint bounded
        var entries = await db.SemanticCacheEntries
            .AsNoTracking()
            .Where(e => e.ExpiresAtTicks > nowTicks)
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            var cached = BlobToFloats(entry.EmbeddingBlob);
            if (CosineSimilarity(questionEmbedding, cached) >= opts.SimilarityThreshold)
                return entry.Answer;
        }

        return null;
    }

    public async Task SetAsync(float[] questionEmbedding, string answer, CancellationToken ct = default)
    {
        if (!opts.Enabled) return;

        var now = DateTimeOffset.UtcNow;
        db.SemanticCacheEntries.Add(new SemanticCacheEntity
        {
            Id             = Guid.NewGuid().ToString(),
            EmbeddingBlob  = FloatsToBlob(questionEmbedding),
            Answer         = answer,
            CreatedAtTicks = now.Ticks,
            ExpiresAtTicks = now.AddSeconds(opts.TtlSeconds).Ticks
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await db.SemanticCacheEntries.ExecuteDeleteAsync(ct);
    }

    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        var nowTicks = DateTimeOffset.UtcNow.Ticks;
        return await db.SemanticCacheEntries
            .CountAsync(e => e.ExpiresAtTicks > nowTicks, ct);
    }

    // --- helpers ---

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < float.Epsilon ? 0f : dot / denom;
    }

    private static byte[] FloatsToBlob(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        MemoryMarshal.Cast<float, byte>(floats).CopyTo(bytes);
        return bytes;
    }

    private static float[] BlobToFloats(byte[] blob)
    {
        var floats = new float[blob.Length / sizeof(float)];
        MemoryMarshal.Cast<byte, float>(blob).CopyTo(floats);
        return floats;
    }
}
