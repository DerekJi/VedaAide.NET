using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// Azure CosmosDB for NoSQL 向量存储实现。
/// 使用 DiskANN 近似最近邻索引（余弦距离）进行向量检索。
/// Partition Key = /documentId，向量维度通过 CosmosDbOptions.EmbeddingDimensions 配置。
/// </summary>
public sealed class CosmosDbVectorStore : IVectorStore
{
    private readonly Container _container;
    private readonly ILogger<CosmosDbVectorStore> _logger;

    /// <summary>向量检索时获取候选数量的倍数，剩余过滤在内存中完成。</summary>
    private const int CandidateMultiplier = 4;

    public CosmosDbVectorStore(
        CosmosClient client,
        CosmosDbOptions options,
        ILogger<CosmosDbVectorStore> logger)
    {
        var opts = options;
        _container = client.GetDatabase(opts.DatabaseName).GetContainer(opts.ChunksContainerName);
        _logger = logger;
    }

    public Task UpsertAsync(DocumentChunk chunk, CancellationToken ct = default)
        => UpsertBatchAsync([chunk], ct);

    public async Task UpsertBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
    {
        var candidates = chunks
            .Select(c => (Chunk: c, Hash: ComputeHash(c.Content)))
            .ToList();

        if (candidates.Count == 0) return;

        // Check for existing content hashes to avoid near-duplicate storage
        var hashesJson = JsonSerializer.Serialize(candidates.Select(c => (object)c.Hash).ToList());
        var checkSql = $"SELECT VALUE c.contentHash FROM c WHERE ARRAY_CONTAINS({hashesJson}, c.contentHash, false)";
        var checkQuery = new QueryDefinition(checkSql);

        var existingHashes = new HashSet<string>(StringComparer.Ordinal);
        var checkFeed = _container.GetItemQueryIterator<string>(checkQuery);
        while (checkFeed.HasMoreResults)
        {
            var page = await checkFeed.ReadNextAsync(ct);
            foreach (var hash in page)
                existingHashes.Add(hash);
        }

        var toInsert = candidates.Where(c => !existingHashes.Contains(c.Hash)).ToList();
        if (toInsert.Count == 0) return;

        foreach (var (chunk, hash) in toInsert)
        {
            ct.ThrowIfCancellationRequested();
            var doc = ToDocument(chunk, hash);
            try
            {
                await _container.UpsertItemAsync(
                    doc,
                    new PartitionKey(chunk.DocumentId),
                    cancellationToken: ct);
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "CosmosDbVectorStore: failed to upsert chunk {Id}", chunk.Id);
                throw;
            }
        }

        _logger.LogDebug("CosmosDbVectorStore: inserted {Count}/{Total} chunks", toInsert.Count, candidates.Count);
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
        var candidateCount = Math.Max(topK * CandidateMultiplier, 20);
        var vecJson = VectorToJson(queryEmbedding);

        // Build optional WHERE conditions for scalar filters
        var conditions = new List<string>
        {
            "c.supersededAtTicks = 0"  // 只检索当前有效块
        };
        if (filterType.HasValue) conditions.Add("c.documentType = @filterType");
        if (dateFrom.HasValue)   conditions.Add("c.createdAtTicks >= @dateFrom");
        if (dateTo.HasValue)     conditions.Add("c.createdAtTicks <= @dateTo");
        AppendScopeConditions(conditions, scope);

        var whereClause = conditions.Count > 0
            ? " WHERE " + string.Join(" AND ", conditions)
            : string.Empty;

        // VectorDistance with cosine returns distance in [0,2]: smaller = more similar
        // ORDER BY ASC gives most-similar results first (DiskANN ANN search)
        var sql = $"""
            SELECT TOP {candidateCount}
                c.id, c.documentId, c.documentName, c.documentType,
                c.content, c.chunkIndex, c.contentHash, c.embeddingModel, c.metadata, c.createdAtTicks,
                VectorDistance(c.embedding, {vecJson}, false) AS distance
            FROM c{whereClause}
            ORDER BY VectorDistance(c.embedding, {vecJson}, false)
            """;

        var queryDef = new QueryDefinition(sql);
        if (filterType.HasValue) queryDef = queryDef.WithParameter("@filterType", (int)filterType.Value);
        if (dateFrom.HasValue)   queryDef = queryDef.WithParameter("@dateFrom", dateFrom.Value.UtcTicks);
        if (dateTo.HasValue)     queryDef = queryDef.WithParameter("@dateTo", dateTo.Value.UtcTicks);
        BindScopeParameters(ref queryDef, scope);

        var results = new List<(DocumentChunk, float)>();
        var feed = _container.GetItemQueryIterator<CosmosSearchResult>(queryDef);

        while (feed.HasMoreResults && results.Count < topK)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var item in page)
            {
                // Convert cosine distance → similarity: similarity ≈ 1 - distance (for normalized vectors)
                var similarity = 1.0f - (float)item.Distance;
                if (similarity < minSimilarity) continue;

                results.Add((ToChunk(item), similarity));
                if (results.Count >= topK) break;
            }
        }

        return results.AsReadOnly();
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
        var keywords = ExtractKeywords(query);
        if (keywords.Count == 0)
            return Array.Empty<(DocumentChunk, float)>();

        var candidateCount = Math.Max(topK * CandidateMultiplier, 20);
        var conditions = new List<string>
        {
            "c.supersededAtTicks = 0"  // 只检索当前有效块
        };
        if (filterType.HasValue) conditions.Add("c.documentType = @filterType");
        if (dateFrom.HasValue)   conditions.Add("c.createdAtTicks >= @dateFrom");
        if (dateTo.HasValue)     conditions.Add("c.createdAtTicks <= @dateTo");
        AppendScopeConditions(conditions, scope);

        // CONTAINS-based keyword match (BM25 平替)
        var keywordExpr = string.Join(" OR ", keywords.Select((_, i) => $"CONTAINS(LOWER(c.content), @kw{i})"));
        conditions.Add($"({keywordExpr})");

        var whereClause = " WHERE " + string.Join(" AND ", conditions);
        var sql = $"""
            SELECT TOP {candidateCount}
                c.id, c.documentId, c.documentName, c.documentType,
                c.content, c.chunkIndex, c.contentHash, c.embeddingModel, c.metadata, c.createdAtTicks
            FROM c{whereClause}
            """;

        var queryDef = new QueryDefinition(sql);
        if (filterType.HasValue) queryDef = queryDef.WithParameter("@filterType", (int)filterType.Value);
        if (dateFrom.HasValue)   queryDef = queryDef.WithParameter("@dateFrom", dateFrom.Value.UtcTicks);
        if (dateTo.HasValue)     queryDef = queryDef.WithParameter("@dateTo", dateTo.Value.UtcTicks);
        BindScopeParameters(ref queryDef, scope);
        for (var i = 0; i < keywords.Count; i++)
            queryDef = queryDef.WithParameter($"@kw{i}", keywords[i].ToLowerInvariant());

        var results = new List<(DocumentChunk, float)>();
        var feed = _container.GetItemQueryIterator<CosmosChunkDocument>(queryDef);

        while (feed.HasMoreResults && results.Count < topK)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var item in page)
            {
                var chunk = new DocumentChunk
                {
                    Id             = item.Id,
                    DocumentId     = item.DocumentId,
                    DocumentName   = item.DocumentName,
                    DocumentType   = (DocumentType)item.DocumentType,
                    Content        = item.Content,
                    ChunkIndex     = item.ChunkIndex,
                    EmbeddingModel = item.EmbeddingModel,
                    Metadata       = item.Metadata,
                    CreatedAt      = new DateTimeOffset(item.CreatedAtTicks, TimeSpan.Zero)
                };
                // Score = keyword coverage ratio (matched keywords / total keywords)
                var matchCount = keywords.Count(kw =>
                    item.Content.Contains(kw, StringComparison.OrdinalIgnoreCase));
                var score = (float)matchCount / keywords.Count;
                results.Add((chunk, score));
                if (results.Count >= topK) break;
            }
        }

        return results
            .OrderByDescending(x => x.Item2)
            .ToList()
            .AsReadOnly();
    }

    public async Task<bool> ExistsAsync(string contentHash, CancellationToken ct = default)
    {
        var queryDef = new QueryDefinition(
            "SELECT VALUE COUNT(1) FROM c WHERE c.contentHash = @hash")
            .WithParameter("@hash", contentHash);

        var feed = _container.GetItemQueryIterator<int>(queryDef);
        if (!feed.HasMoreResults) return false;

        var page = await feed.ReadNextAsync(ct);
        return page.FirstOrDefault() > 0;
    }

    public async Task DeleteByDocumentAsync(string documentId, CancellationToken ct = default)
    {
        var partitionKey = new PartitionKey(documentId);
        var queryDef = new QueryDefinition("SELECT c.id FROM c");
        var feed = _container.GetItemQueryIterator<CosmosIdOnly>(
            queryDef,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var item in page)
            {
                ct.ThrowIfCancellationRequested();
                await _container.DeleteItemAsync<CosmosChunkDocument>(
                    item.Id, partitionKey, cancellationToken: ct);
            }
        }

        _logger.LogDebug("CosmosDbVectorStore: deleted all chunks for documentId={DocumentId}", documentId);
    }

    public async Task<IReadOnlyList<DocumentChunk>> GetCurrentChunksByDocumentNameAsync(
        string documentName, CancellationToken ct = default)
    {
        var queryDef = new QueryDefinition(
            "SELECT c.id, c.documentId, c.documentName, c.documentType, c.content, c.chunkIndex, c.embeddingModel, c.metadata, c.createdAtTicks, c.version FROM c WHERE c.documentName = @name AND c.supersededAtTicks = 0 ORDER BY c.chunkIndex")
            .WithParameter("@name", documentName);

        var result = new List<DocumentChunk>();
        var feed = _container.GetItemQueryIterator<CosmosChunkDocument>(queryDef);
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var item in page)
                result.Add(new DocumentChunk
                {
                    Id = item.Id, DocumentId = item.DocumentId,
                    DocumentName = item.DocumentName, DocumentType = (DocumentType)item.DocumentType,
                    Content = item.Content, ChunkIndex = item.ChunkIndex,
                    EmbeddingModel = item.EmbeddingModel, Metadata = item.Metadata,
                    CreatedAt = new DateTimeOffset(item.CreatedAtTicks, TimeSpan.Zero),
                    Version = item.Version
                });
        }
        return result;
    }

    public async Task MarkDocumentSupersededAsync(
        string documentName, string newDocumentId, CancellationToken ct = default)
    {
        // CosmosDB doesn't support UPDATE; must read → patch each document
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var queryDef = new QueryDefinition(
            "SELECT c.id, c.documentId FROM c WHERE c.documentName = @name AND c.supersededAtTicks = 0")
            .WithParameter("@name", documentName);

        var feed = _container.GetItemQueryIterator<CosmosIdOnly>(queryDef);
        var patchOps = new[]
        {
            PatchOperation.Set("/supersededAtTicks", now),
            PatchOperation.Set("/supersededByDocId", newDocumentId)
        };

        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var item in page)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // PartitionKey = /documentId — 必须与 item 中的 documentId 精确匹配。
                    // 使用 PartitionKey.None 会导致跨分区更新被 CosmosDB 拒绝（403/404）。
                    await _container.PatchItemAsync<CosmosChunkDocument>(
                        item.Id, new PartitionKey(item.DocumentId), patchOps, cancellationToken: ct);
                }
                catch (CosmosException ex)
                {
                    _logger.LogWarning(ex, "Failed to patch chunk {Id} for supersession", item.Id);
                }
            }
        }
    }

    public async Task<IReadOnlyList<DocumentVersionInfo>> GetVersionHistoryAsync(
        string documentName, CancellationToken ct = default)
    {
        var queryDef = new QueryDefinition(
            "SELECT c.documentId, c.version, c.createdAtTicks, c.supersededAtTicks FROM c WHERE c.documentName = @name ORDER BY c.version")
            .WithParameter("@name", documentName);

        var groups = new Dictionary<(string DocId, int Version), (int Count, long MinCreated, long MaxSuperseded)>();
        var feed = _container.GetItemQueryIterator<CosmosVersionRow>(queryDef);
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var row in page)
            {
                var key = (row.DocumentId, row.Version);
                if (groups.TryGetValue(key, out var existing))
                    groups[key] = (existing.Count + 1, Math.Min(existing.MinCreated, row.CreatedAtTicks), Math.Max(existing.MaxSuperseded, row.SupersededAtTicks));
                else
                    groups[key] = (1, row.CreatedAtTicks, row.SupersededAtTicks);
            }
        }

        return groups
            .OrderBy(kv => kv.Key.Version)
            .Select(kv => new DocumentVersionInfo(
                kv.Key.DocId, documentName, kv.Key.Version, kv.Value.Count,
                new DateTimeOffset(kv.Value.MinCreated, TimeSpan.Zero),
                kv.Value.MaxSuperseded > 0 ? new DateTimeOffset(kv.Value.MaxSuperseded, TimeSpan.Zero) : null))
            .ToList();
    }

    public async Task<IReadOnlyList<DocumentSummary>> GetAllDocumentsAsync(CancellationToken ct = default)
    {
        // 仅查询 documentId/documentName/documentType，不加载 content 和 embedding
        var queryDef = new QueryDefinition(
            "SELECT c.documentId, c.documentName, c.documentType FROM c WHERE c.supersededAtTicks = 0");

        var rows = new List<CosmosDocRow>();
        var feed = _container.GetItemQueryIterator<CosmosDocRow>(queryDef);
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            rows.AddRange(page);
        }

        return rows
            .GroupBy(r => new { r.DocumentId, r.DocumentName, r.DocumentType })
            .Select(g => new DocumentSummary(
                g.Key.DocumentId,
                g.Key.DocumentName,
                (DocumentType)g.Key.DocumentType,
                g.Count()))
            .OrderBy(s => s.DocumentName)
            .ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static CosmosChunkDocument ToDocument(DocumentChunk chunk, string hash) => new()
    {
        Id             = chunk.Id,
        DocumentId     = chunk.DocumentId,
        DocumentName   = chunk.DocumentName,
        DocumentType   = (int)chunk.DocumentType,
        Content        = chunk.Content,
        ChunkIndex     = chunk.ChunkIndex,
        ContentHash    = hash,
        Embedding      = chunk.Embedding ?? [],
        EmbeddingModel = chunk.EmbeddingModel,
        Metadata       = chunk.Metadata,
        CreatedAtTicks = chunk.CreatedAt.UtcTicks,
        Version        = chunk.Version,
        SupersededAtTicks  = chunk.SupersededAt.HasValue ? chunk.SupersededAt.Value.UtcTicks : 0,
        SupersededByDocId  = chunk.SupersededBy ?? string.Empty
    };

    private static DocumentChunk ToChunk(CosmosSearchResult r) => new()
    {
        Id             = r.Id,
        DocumentId     = r.DocumentId,
        DocumentName   = r.DocumentName,
        DocumentType   = (DocumentType)r.DocumentType,
        Content        = r.Content,
        ChunkIndex     = r.ChunkIndex,
        EmbeddingModel = r.EmbeddingModel,
        Metadata       = r.Metadata,
        CreatedAt      = new DateTimeOffset(r.CreatedAtTicks, TimeSpan.Zero),
        Version        = r.Version,
        SupersededAt   = r.SupersededAtTicks > 0
            ? new DateTimeOffset(r.SupersededAtTicks, TimeSpan.Zero)
            : null
    };

    /// <summary>
    /// 将 float[] 序列化为 CosmosDB SQL 内联向量格式，例如 [0.1,-0.3,0.7]。
    /// 使用 G7 格式确保精度与 CosmosDB 兼容，并使用 InvariantCulture 避免本地化问题。
    /// </summary>
    private static string VectorToJson(float[] v)
    {
        var sb = new StringBuilder("[", v.Length * 10);
        for (var i = 0; i < v.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(v[i].ToString("G7", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 将 KnowledgeScope 字段追加为 WHERE 子句条件。
    /// 作用域元数据作为带 "_scope_" 前缀的 key 存储在 metadata 字典中。
    /// </summary>
    private static void AppendScopeConditions(List<string> conditions, KnowledgeScope? scope)
    {
        if (scope is null) return;
        if (scope.Domain     is not null) conditions.Add("c.metadata[\"_scope_domain\"] = @scopeDomain");
        if (scope.OwnerId    is not null) conditions.Add("c.metadata[\"_scope_ownerId\"] = @scopeOwnerId");
        if (scope.SourceType is not null) conditions.Add("c.metadata[\"_scope_sourceType\"] = @scopeSourceType");
        if (scope.Visibility is not null)
        {
            // Public 过滤：历史文档（无 visibility 元数据）也视为 Public
            if (scope.Visibility == Visibility.Public)
                conditions.Add("(NOT IS_DEFINED(c.metadata[\"_scope_visibility\"]) OR c.metadata[\"_scope_visibility\"] = @scopeVisibility)");
            else
                conditions.Add("c.metadata[\"_scope_visibility\"] = @scopeVisibility");
        }
    }

    private static void BindScopeParameters(ref QueryDefinition queryDef, KnowledgeScope? scope)
    {
        if (scope is null) return;
        if (scope.Domain     is not null) queryDef = queryDef.WithParameter("@scopeDomain",     scope.Domain);
        if (scope.OwnerId    is not null) queryDef = queryDef.WithParameter("@scopeOwnerId",    scope.OwnerId);
        if (scope.SourceType is not null) queryDef = queryDef.WithParameter("@scopeSourceType", scope.SourceType);
        if (scope.Visibility is not null) queryDef = queryDef.WithParameter("@scopeVisibility", scope.Visibility.ToString());
    }

    /// <summary>从查询字符串提取有意义的关键词（过滤停用词和短词）。</summary>
    private static List<string> ExtractKeywords(string query)
        => query
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10) // 限制关键词数量，避免 SQL 过长
            .ToList();
}
