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
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        KnowledgeScope? scope = null,
        CancellationToken ct = default)
    {
        var query = db.VectorChunks.AsNoTracking()
            .Where(x => x.SupersededAtTicks == 0);  // 只检索当前有效块
        if (filterType.HasValue)
            query = query.Where(x => x.DocumentType == (int)filterType.Value);
        if (dateFrom.HasValue)
            query = query.Where(x => x.CreatedAtTicks >= dateFrom.Value.UtcTicks);
        if (dateTo.HasValue)
            query = query.Where(x => x.CreatedAtTicks <= dateTo.Value.UtcTicks);

        var candidates = await query
            .Select(x => new { x.Id, x.DocumentId, x.Content, x.DocumentName, x.DocumentType, x.ChunkIndex, x.EmbeddingBlob, x.EmbeddingModel, x.MetadataJson, x.CreatedAtTicks, x.Version })
            .ToListAsync(ct);

        return candidates
            .Select(e =>
            {
                var embedding = BlobToFloats(e.EmbeddingBlob);
                var similarity = VectorMath.CosineSimilarity(queryEmbedding, embedding);
                var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(e.MetadataJson) ?? new();
                return (
                    Chunk: new DocumentChunk
                    {
                        Id = e.Id,
                        DocumentId = e.DocumentId,
                        Content = e.Content,
                        DocumentName = e.DocumentName,
                        DocumentType = (DocumentType)e.DocumentType,
                        ChunkIndex = e.ChunkIndex,
                        Embedding = embedding,
                        EmbeddingModel = e.EmbeddingModel,
                        Metadata = metadata,
                        CreatedAt = new DateTimeOffset(e.CreatedAtTicks, TimeSpan.Zero),
                        Version = e.Version,
                        Scope = ReadScope(metadata)
                    },
                    Similarity: similarity
                );
            })
            .Where(x => x.Similarity >= minSimilarity)
            .Where(x => MatchesScope(x.Chunk.Scope, scope))
            .OrderByDescending(x => x.Similarity)
            .Take(topK)
            .ToList();
    }

    public async Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> SearchByKeywordsAsync(
        string query,
        int topK = 5,
        DocumentType? filterType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        KnowledgeScope? scope = null,
        CancellationToken ct = default)
    {
        var keywords = query
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        if (keywords.Count == 0)
            return Array.Empty<(DocumentChunk, float)>();

        var dbQuery = db.VectorChunks.AsNoTracking()
            .Where(x => x.SupersededAtTicks == 0);  // 只检索当前有效块
        if (filterType.HasValue)
            dbQuery = dbQuery.Where(x => x.DocumentType == (int)filterType.Value);
        if (dateFrom.HasValue)
            dbQuery = dbQuery.Where(x => x.CreatedAtTicks >= dateFrom.Value.UtcTicks);
        if (dateTo.HasValue)
            dbQuery = dbQuery.Where(x => x.CreatedAtTicks <= dateTo.Value.UtcTicks);

        // SQLite LIKE-based keyword search (case-insensitive via EF Core)
        var lowerKeywords = keywords.Select(k => k.ToLowerInvariant()).ToList();
        dbQuery = dbQuery.Where(x => lowerKeywords.Any(kw => EF.Functions.Like(x.Content.ToLower(), $"%{kw}%")));

        var entities = await dbQuery
            .Select(x => new { x.Id, x.DocumentId, x.Content, x.DocumentName, x.DocumentType, x.ChunkIndex, x.EmbeddingModel, x.MetadataJson, x.CreatedAtTicks, x.Version })
            .ToListAsync(ct);

        return entities
            .Select(e =>
            {
                var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(e.MetadataJson) ?? new();
                var chunk = new DocumentChunk
                {
                    Id             = e.Id,
                    DocumentId     = e.DocumentId,
                    Content        = e.Content,
                    DocumentName   = e.DocumentName,
                    DocumentType   = (DocumentType)e.DocumentType,
                    ChunkIndex     = e.ChunkIndex,
                    EmbeddingModel = e.EmbeddingModel,
                    Metadata       = metadata,
                    CreatedAt      = new DateTimeOffset(e.CreatedAtTicks, TimeSpan.Zero),
                    Version        = e.Version,
                    Scope          = ReadScope(metadata)
                };
                var matchCount = lowerKeywords.Count(kw =>
                    e.Content.Contains(kw, StringComparison.OrdinalIgnoreCase));
                var score = (float)matchCount / lowerKeywords.Count;
                return (Chunk: chunk, Score: score);
            })
            .Where(x => MatchesScope(x.Chunk.Scope, scope))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
    }

    public async Task<bool> ExistsAsync(string contentHash, CancellationToken ct = default)
        => await db.VectorChunks.AnyAsync(x => x.ContentHash == contentHash, ct);

    public async Task DeleteByDocumentAsync(string documentId, CancellationToken ct = default)
    {
        await db.VectorChunks.Where(x => x.DocumentId == documentId).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetCurrentChunksByDocumentNameAsync(
        string documentName, CancellationToken ct = default)
    {
        var entities = await db.VectorChunks.AsNoTracking()
            .Where(x => x.DocumentName == documentName && x.SupersededAtTicks == 0)
            .OrderBy(x => x.ChunkIndex)
            .Select(x => new { x.Id, x.DocumentId, x.DocumentName, x.DocumentType, x.Content, x.ChunkIndex, x.EmbeddingModel, x.MetadataJson, x.CreatedAtTicks, x.Version })
            .ToListAsync(ct);

        return entities.Select(e =>
        {
            var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(e.MetadataJson) ?? new();
            return new DocumentChunk
            {
                Id = e.Id,
                DocumentId = e.DocumentId,
                DocumentName = e.DocumentName,
                DocumentType = (DocumentType)e.DocumentType,
                Content = e.Content,
                ChunkIndex = e.ChunkIndex,
                EmbeddingModel = e.EmbeddingModel,
                Metadata = metadata,
                CreatedAt = new DateTimeOffset(e.CreatedAtTicks, TimeSpan.Zero),
                Version = e.Version,
                Scope = ReadScope(metadata)
            };
        }).ToList();
    }

    public async Task MarkDocumentSupersededAsync(
        string documentName, string newDocumentId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        await db.VectorChunks
            .Where(x => x.DocumentName == documentName && x.SupersededAtTicks == 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SupersededAtTicks, now)
                .SetProperty(x => x.SupersededByDocId, newDocumentId), ct);
    }

    public async Task<IReadOnlyList<DocumentVersionInfo>> GetVersionHistoryAsync(
        string documentName, CancellationToken ct = default)
    {
        var groups = await db.VectorChunks.AsNoTracking()
            .Where(x => x.DocumentName == documentName)
            .GroupBy(x => new { x.DocumentId, x.Version })
            .Select(g => new
            {
                g.Key.DocumentId,
                g.Key.Version,
                ChunkCount = g.Count(),
                MinCreatedAtTicks = g.Min(x => x.CreatedAtTicks),
                MaxSupersededAtTicks = g.Max(x => x.SupersededAtTicks)
            })
            .OrderBy(g => g.Version)
            .ToListAsync(ct);

        return groups.Select(g => new DocumentVersionInfo(
            g.DocumentId,
            documentName,
            g.Version,
            g.ChunkCount,
            new DateTimeOffset(g.MinCreatedAtTicks, TimeSpan.Zero),
            g.MaxSupersededAtTicks > 0
                ? new DateTimeOffset(g.MaxSupersededAtTicks, TimeSpan.Zero)
                : null
        )).ToList();
    }

    public async Task<IReadOnlyList<DocumentSummary>> GetAllDocumentsAsync(
        KnowledgeScope? scope = null,
        CancellationToken ct = default)
    {
        var query = db.VectorChunks.AsNoTracking()
            .Where(x => x.SupersededAtTicks == 0);

        // Filter by OwnerId when scope is provided — prevents cross-user document leakage.
        if (scope?.OwnerId is not null)
        {
            var ownerTag = $"\"_scope_ownerId\":\"{scope.OwnerId}\"";
            query = query.Where(x => x.MetadataJson.Contains(ownerTag));
        }

        var groups = await query
            .GroupBy(x => new { x.DocumentId, x.DocumentName, x.DocumentType })
            .Select(g => new
            {
                g.Key.DocumentId,
                g.Key.DocumentName,
                g.Key.DocumentType,
                ChunkCount = g.Count()
            })
            .OrderBy(g => g.DocumentName)
            .ToListAsync(ct);

        return groups.Select(g => new DocumentSummary(
            g.DocumentId,
            g.DocumentName,
            (DocumentType)g.DocumentType,
            g.ChunkCount)).ToList();
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
        EmbeddingModel = chunk.EmbeddingModel,
        MetadataJson = JsonSerializer.Serialize(chunk.Metadata),
        CreatedAtTicks = chunk.CreatedAt.UtcTicks,
        Version = chunk.Version,
        SupersededAtTicks = chunk.SupersededAt.HasValue ? chunk.SupersededAt.Value.UtcTicks : 0,
        SupersededByDocId = chunk.SupersededBy ?? string.Empty
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

    /// <summary>从 metadata 字典中读取 KnowledgeScope 字段。</summary>
    private static KnowledgeScope? ReadScope(Dictionary<string, string> metadata)
    {
        if (!metadata.ContainsKey("_scope_domain") &&
            !metadata.ContainsKey("_scope_ownerId") &&
            !metadata.ContainsKey("_scope_sourceType") &&
            !metadata.ContainsKey("_scope_visibility"))
            return null;

        metadata.TryGetValue("_scope_domain",     out var domain);
        metadata.TryGetValue("_scope_ownerId",    out var ownerId);
        metadata.TryGetValue("_scope_sourceType", out var sourceType);
        metadata.TryGetValue("_scope_visibility", out var visStr);
        // null visibility = 历史文档，视为 Public（兼容旧数据）
        Visibility? visibility = Enum.TryParse<Visibility>(visStr, out var v) ? v : null;
        return new KnowledgeScope(domain, sourceType, null, null, null, ownerId, visibility);
    }

    /// <summary>判断 chunk 的 scope 是否满足过滤条件（null scope 不过滤）。</summary>
    private static bool MatchesScope(KnowledgeScope? chunkScope, KnowledgeScope? filterScope)
    {
        if (filterScope is null) return true;
        if (filterScope.Domain     is not null && !string.Equals(chunkScope?.Domain,     filterScope.Domain,     StringComparison.OrdinalIgnoreCase)) return false;
        if (filterScope.OwnerId    is not null && !string.Equals(chunkScope?.OwnerId,    filterScope.OwnerId,    StringComparison.OrdinalIgnoreCase)) return false;
        if (filterScope.SourceType is not null && !string.Equals(chunkScope?.SourceType, filterScope.SourceType, StringComparison.OrdinalIgnoreCase)) return false;
        if (filterScope.Visibility is not null)
        {
            // null visibility = 历史文档，无限制 → 兼容视为 Public
            var effectiveVisibility = chunkScope?.Visibility ?? Visibility.Public;
            if (effectiveVisibility != filterScope.Visibility) return false;
        }
        return true;
    }
}
