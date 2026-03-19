using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// SQLite 向量存储实现。
/// 通过将所有向量加载到内存后执行余弦相似度计算来实现向量检索。
/// 适合中小规模（< 100K 块）场景；大规模时可替换为 Azure AI Search。
/// </summary>
public sealed class SqliteVectorStore(VedaDbContext db) : IVectorStore
{
    // DRY: 委托到 UpsertBatchAsync，不重复哈希+去重逻辑。
    public Task UpsertAsync(DocumentChunk chunk, CancellationToken ct = default)
        => UpsertBatchAsync([chunk], ct);

    public async Task UpsertBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
    {
        // 预先计算所有 hash，然后一次批量查询已存在的 hash，避免 N+1 问题。
        var candidates = chunks
            .Select(c => (Chunk: c, Hash: ComputeHash(c.Content)))
            .ToList();

        if (candidates.Count == 0) return;

        var incomingHashes = candidates.Select(x => x.Hash).ToList();
        var existingHashes = await db.VectorChunks
            .Where(x => incomingHashes.Contains(x.ContentHash))
            .Select(x => x.ContentHash)
            .ToHashSetAsync(ct);

        var toInsert = candidates
            .Where(x => !existingHashes.Contains(x.Hash))
            .Select(x => ToEntity(x.Chunk, x.Hash))
            .ToList();

        if (toInsert.Count > 0)
        {
            db.VectorChunks.AddRange(toInsert);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> SearchAsync(
        float[] queryEmbedding,
        int topK = 5,
        float minSimilarity = 0.6f,
        DocumentType? filterType = null,
        CancellationToken ct = default)
    {
        var query = db.VectorChunks.AsNoTracking();
        if (filterType.HasValue)
            query = query.Where(x => x.DocumentType == (int)filterType.Value);

        var candidates = await query
            .Select(x => new { x.Id, x.Content, x.DocumentName, x.DocumentType, x.ChunkIndex, x.EmbeddingBlob, x.MetadataJson, x.CreatedAtTicks })
            .ToListAsync(ct);

        return candidates
            .Select(e =>
            {
                var embedding = BlobToFloats(e.EmbeddingBlob);
                var similarity = VectorMath.CosineSimilarity(queryEmbedding, embedding);
                return (
                    Chunk: new DocumentChunk
                    {
                        Id = e.Id,
                        Content = e.Content,
                        DocumentName = e.DocumentName,
                        DocumentType = (DocumentType)e.DocumentType,
                        ChunkIndex = e.ChunkIndex,
                        Embedding = embedding,
                        Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(e.MetadataJson) ?? new(),
                        CreatedAt = new DateTimeOffset(e.CreatedAtTicks, TimeSpan.Zero)
                    },
                    Similarity: similarity
                );
            })
            .Where(x => x.Similarity >= minSimilarity)
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .ToList();
    }

    public async Task<bool> ExistsAsync(string contentHash, CancellationToken ct = default)
        => await db.VectorChunks.AnyAsync(x => x.ContentHash == contentHash, ct);

    public async Task DeleteByDocumentAsync(string documentId, CancellationToken ct = default)
    {
        await db.VectorChunks.Where(x => x.DocumentId == documentId).ExecuteDeleteAsync(ct);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static VectorChunkEntity ToEntity(DocumentChunk chunk, string hash) => new()
    {
        Id = chunk.Id,
        DocumentId = chunk.DocumentId,
        DocumentName = chunk.DocumentName,
        DocumentType = (int)chunk.DocumentType,
        Content = chunk.Content,
        ChunkIndex = chunk.ChunkIndex,
        ContentHash = hash,
        EmbeddingBlob = FloatsToBlob(chunk.Embedding ?? []),
        MetadataJson = JsonSerializer.Serialize(chunk.Metadata),
        CreatedAtTicks = chunk.CreatedAt.UtcTicks
    };

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private static byte[] FloatsToBlob(float[] floats)
    {
        var bytes = new byte[floats.Length * sizeof(float)];
        MemoryMarshal.AsBytes(floats.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    private static float[] BlobToFloats(byte[] blob)
    {
        var floats = new float[blob.Length / sizeof(float)];
        MemoryMarshal.Cast<byte, float>(blob).CopyTo(floats);
        return floats;
    }

    // CosineSimilarity 已迁移到 VectorMath（SRP：数学运算不属于存储层）。
}
