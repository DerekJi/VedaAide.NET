using Veda.Core.Options;
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
    private readonly CosmosDbOptions _options;

    /// <summary>向量检索时获取候选数量的倍数，剩余过滤在内存中完成。</summary>
    private const int CandidateMultiplier = 4;

    public CosmosDbVectorStore(
        CosmosClient client,
        CosmosDbOptions options,
        ILogger<CosmosDbVectorStore> logger)
    {
        _options = options;
        _container = client.GetDatabase(options.DatabaseName).GetContainer(options.ChunksContainerName);
        _logger = logger;

        // 检查是否为 Cosmos DB provider，如果不是，则警告 EnableFullTextKeywordSearch 配置无效
        // 目前通过 CosmosClient.Endpoint 判断，若为本地 SQLite，通常 endpoint 为空或为 file://
        var endpoint = options.Endpoint?.ToLowerInvariant() ?? string.Empty;
        if (_options.EnableFullTextKeywordSearch &&
            (string.IsNullOrWhiteSpace(endpoint) || endpoint.StartsWith("file://") || endpoint.Contains("sqlite")))
        {
            _logger.LogWarning("[CosmosDbVectorStore] EnableFullTextKeywordSearch is enabled, but current provider is not Azure Cosmos DB. This setting will be ignored and fallback to CONTAINS scoring.");
        }
    }

    public Task UpsertAsync(DocumentChunk chunk, CancellationToken ct = default)
        => UpsertBatchAsync([chunk], ct);

    public async Task UpsertBatchAsync(IEnumerable<DocumentChunk> chunks, CancellationToken ct = default)
    {
        var candidates = chunks
            .Select(c => (Chunk: c, Hash: ComputeHash(c.Content)))
            .ToList();

        if (candidates.Count == 0) return;

        // 仅检查当前有效（未被 supersede）的 chunks 的哈希，避免被 superseded 的历史数据
        // 阻止相同内容重新写入（例如文档重新上传时需要恢复活跃状态）。
        var hashesJson = JsonSerializer.Serialize(candidates.Select(c => (object)c.Hash).ToList());
        var checkSql = $"SELECT VALUE c.contentHash FROM c WHERE c.supersededAtTicks = 0 AND ARRAY_CONTAINS({hashesJson}, c.contentHash, false)";
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

        // VectorDistance with cosine returns similarity in [-1,1]: larger = more similar.
        // Cosmos DB always sorts most-similar first; specifying ASC or DESC is not allowed.
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
                // VectorDistance cosine returns similarity directly in [-1,1]; higher = more similar
                var similarity = (float)item.Distance;
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

        if (_options.EnableFullTextKeywordSearch)
        {
            var fullTextResults = await TrySearchByFullTextAsync(
                keywords, topK, filterType, dateFrom, dateTo, scope, ct);
            if (fullTextResults.Count > 0)
                return fullTextResults;
        }

        return await SearchByContainsWithTfScoringAsync(
            keywords, topK, filterType, dateFrom, dateTo, scope, ct);
    }

    private async Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> TrySearchByFullTextAsync(
        IReadOnlyList<string> keywords,
        int topK,
        DocumentType? filterType,
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo,
        KnowledgeScope? scope,
        CancellationToken ct)
    {
        try
        {
            var candidateCount = Math.Max(topK * CandidateMultiplier, 20);
            var conditions = new List<string>
            {
                "c.supersededAtTicks = 0"
            };
            if (filterType.HasValue) conditions.Add("c.documentType = @filterType");
            if (dateFrom.HasValue)   conditions.Add("c.createdAtTicks >= @dateFrom");
            if (dateTo.HasValue)     conditions.Add("c.createdAtTicks <= @dateTo");
            AppendScopeConditions(conditions, scope);

            var searchExpr = string.IsNullOrWhiteSpace(_options.FullTextLanguage)
                ? "FullTextContainsAny(c.content, @keywords)"
                : "FullTextContainsAny(c.content, @keywords, @fullTextLanguage)";
            conditions.Add(searchExpr);

            var scoreExpr = string.IsNullOrWhiteSpace(_options.FullTextLanguage)
                ? "FullTextScore(c.content, @keywords)"
                : "FullTextScore(c.content, @keywords, @fullTextLanguage)";

            var whereClause = " WHERE " + string.Join(" AND ", conditions);
            var sql = $"""
                SELECT TOP {candidateCount}
                    c.id, c.documentId, c.documentName, c.documentType,
                    c.content, c.chunkIndex, c.contentHash, c.embeddingModel, c.metadata, c.createdAtTicks,
                    c.version, c.supersededAtTicks,
                    {scoreExpr} AS bm25Score
                FROM c{whereClause}
                ORDER BY RANK {scoreExpr}
                """;

            var queryDef = new QueryDefinition(sql)
                .WithParameter("@keywords", keywords.ToArray());
            if (filterType.HasValue) queryDef = queryDef.WithParameter("@filterType", (int)filterType.Value);
            if (dateFrom.HasValue)   queryDef = queryDef.WithParameter("@dateFrom", dateFrom.Value.UtcTicks);
            if (dateTo.HasValue)     queryDef = queryDef.WithParameter("@dateTo", dateTo.Value.UtcTicks);
            if (!string.IsNullOrWhiteSpace(_options.FullTextLanguage))
                queryDef = queryDef.WithParameter("@fullTextLanguage", _options.FullTextLanguage);
            BindScopeParameters(ref queryDef, scope);

            var results = new List<(DocumentChunk, float)>();
            var feed = _container.GetItemQueryIterator<CosmosKeywordSearchResult>(queryDef);

            while (feed.HasMoreResults && results.Count < topK)
            {
                var page = await feed.ReadNextAsync(ct);
                foreach (var item in page)
                {
                    var score = (float)Math.Max(item.Bm25Score, 0d);
                    results.Add((ToChunk(item), score));
                    if (results.Count >= topK) break;
                }
            }

            return results
                .OrderByDescending(x => x.Item2)
                .ToList()
                .AsReadOnly();
        }
        catch (CosmosException ex)
        {
            _logger.LogWarning(ex,
                "CosmosDbVectorStore: FullText keyword search unavailable, fallback to CONTAINS scoring");
            return Array.Empty<(DocumentChunk, float)>();
        }
    }

    private async Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> SearchByContainsWithTfScoringAsync(
        IReadOnlyList<string> keywords,
        int topK,
        DocumentType? filterType,
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo,
        KnowledgeScope? scope,
        CancellationToken ct)
    {
        var lowerKeywords = keywords
            .Select(k => k.ToLowerInvariant())
            .ToList();

        var candidateCount = Math.Max(topK * CandidateMultiplier, 20);
        var conditions = new List<string>
        {
            "c.supersededAtTicks = 0"  // 只检索当前有效块
        };
        if (filterType.HasValue) conditions.Add("c.documentType = @filterType");
        if (dateFrom.HasValue)   conditions.Add("c.createdAtTicks >= @dateFrom");
        if (dateTo.HasValue)     conditions.Add("c.createdAtTicks <= @dateTo");
        AppendScopeConditions(conditions, scope);

        var keywordExpr = string.Join(" OR ", lowerKeywords.Select((_, i) => $"CONTAINS(LOWER(c.content), @kw{i})"));
        conditions.Add($"({keywordExpr})");

        var whereClause = " WHERE " + string.Join(" AND ", conditions);
        var sql = $"""
            SELECT TOP {candidateCount}
                c.id, c.documentId, c.documentName, c.documentType,
                c.content, c.chunkIndex, c.contentHash, c.embeddingModel, c.metadata, c.createdAtTicks,
                c.version, c.supersededAtTicks
            FROM c{whereClause}
            """;

        var queryDef = new QueryDefinition(sql);
        if (filterType.HasValue) queryDef = queryDef.WithParameter("@filterType", (int)filterType.Value);
        if (dateFrom.HasValue)   queryDef = queryDef.WithParameter("@dateFrom", dateFrom.Value.UtcTicks);
        if (dateTo.HasValue)     queryDef = queryDef.WithParameter("@dateTo", dateTo.Value.UtcTicks);
        BindScopeParameters(ref queryDef, scope);
        for (var i = 0; i < lowerKeywords.Count; i++)
            queryDef = queryDef.WithParameter($"@kw{i}", lowerKeywords[i]);

        var results = new List<(DocumentChunk, float)>();
        var feed = _container.GetItemQueryIterator<CosmosChunkDocument>(queryDef);

        while (feed.HasMoreResults && results.Count < topK)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var item in page)
            {
                var score = CalculateContainsScore(item.Content, lowerKeywords);
                if (score <= 0f) continue;

                results.Add((ToChunk(item), score));
                if (results.Count >= topK) break;
            }
        }

        return results
            .OrderByDescending(x => x.Item2)
            .Take(topK)
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

    public async Task<int> ClearAllAsync(CancellationToken ct = default)
    {
        // CosmosDB 不支持 TRUNCATE；必须逐条查询后删除。
        // 跨分区查询 SELECT c.id, c.documentId，然后按 partitionKey 逐条删除。
        var queryDef = new QueryDefinition("SELECT c.id, c.documentId FROM c");
        var feed = _container.GetItemQueryIterator<CosmosIdOnly>(queryDef);

        var deleted = 0;
        while (feed.HasMoreResults)
        {
            var page = await feed.ReadNextAsync(ct);
            foreach (var item in page)
            {
                ct.ThrowIfCancellationRequested();
                await _container.DeleteItemAsync<CosmosChunkDocument>(
                    item.Id, new PartitionKey(item.DocumentId), cancellationToken: ct);
                deleted++;
            }
        }

        _logger.LogWarning("CosmosDbVectorStore: cleared {Count} chunks", deleted);
        return deleted;
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

    public async Task<IReadOnlyList<DocumentSummary>> GetAllDocumentsAsync(
        KnowledgeScope? scope = null,
        CancellationToken ct = default)
    {
        // Build query — filter by _scope_ownerId when scope is provided.
        string sql;
        QueryDefinition queryDef;
        if (scope?.OwnerId is not null)
        {
            // Include legacy documents (no _scope_ownerId) for backward compatibility.
            sql = "SELECT c.documentId, c.documentName, c.documentType FROM c WHERE c.supersededAtTicks = 0 AND (NOT IS_DEFINED(c.metadata._scope_ownerId) OR c.metadata._scope_ownerId = @ownerId)";
            queryDef = new QueryDefinition(sql).WithParameter("@ownerId", scope.OwnerId);
        }
        else
        {
            sql = "SELECT c.documentId, c.documentName, c.documentType FROM c WHERE c.supersededAtTicks = 0";
            queryDef = new QueryDefinition(sql);
        }

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

    private static DocumentChunk ToChunk(CosmosChunkDocument d) => new()
    {
        Id             = d.Id,
        DocumentId     = d.DocumentId,
        DocumentName   = d.DocumentName,
        DocumentType   = (DocumentType)d.DocumentType,
        Content        = d.Content,
        ChunkIndex     = d.ChunkIndex,
        EmbeddingModel = d.EmbeddingModel,
        Metadata       = d.Metadata,
        CreatedAt      = new DateTimeOffset(d.CreatedAtTicks, TimeSpan.Zero),
        Version        = d.Version,
        SupersededAt   = d.SupersededAtTicks > 0
            ? new DateTimeOffset(d.SupersededAtTicks, TimeSpan.Zero)
            : null
    };

    private static DocumentChunk ToChunk(CosmosKeywordSearchResult d) => new()
    {
        Id             = d.Id,
        DocumentId     = d.DocumentId,
        DocumentName   = d.DocumentName,
        DocumentType   = (DocumentType)d.DocumentType,
        Content        = d.Content,
        ChunkIndex     = d.ChunkIndex,
        EmbeddingModel = d.EmbeddingModel,
        Metadata       = d.Metadata,
        CreatedAt      = new DateTimeOffset(d.CreatedAtTicks, TimeSpan.Zero),
        Version        = d.Version,
        SupersededAt   = d.SupersededAtTicks > 0
            ? new DateTimeOffset(d.SupersededAtTicks, TimeSpan.Zero)
            : null
    };

    private static float CalculateContainsScore(string content, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(content) || keywords.Count == 0)
            return 0f;

        var normalized = content.ToLowerInvariant();
        var wordCount = CountWords(normalized);
        if (wordCount == 0)
            return 0f;

        var matchedKeywordCount = 0;
        var totalOccurrences = 0;
        foreach (var keyword in keywords)
        {
            var occurrences = CountOccurrences(normalized, keyword);
            if (occurrences <= 0) continue;

            matchedKeywordCount++;
            totalOccurrences += occurrences;
        }

        if (matchedKeywordCount == 0)
            return 0f;

        var coverage = (float)matchedKeywordCount / keywords.Count;
        var avgTermFrequency = (float)totalOccurrences / wordCount;
        var tfScore = MathF.Min(avgTermFrequency * 20f, 1f);

        return (coverage * 0.7f) + (tfScore * 0.3f);
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (inWord) continue;
                inWord = true;
                count++;
            }
            else
            {
                inWord = false;
            }
        }

        return count;
    }

    private static int CountOccurrences(string text, string keyword)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword))
            return 0;

        var count = 0;
        var index = 0;
        while (true)
        {
            index = text.IndexOf(keyword, index, StringComparison.Ordinal);
            if (index < 0) break;

            count++;
            index += keyword.Length;
        }

        return count;
    }

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
        // Legacy data has no _scope_ownerId — treat as visible to all authenticated users (backward compat).
        if (scope.OwnerId    is not null) conditions.Add("(NOT IS_DEFINED(c.metadata[\"_scope_ownerId\"]) OR c.metadata[\"_scope_ownerId\"] = @scopeOwnerId)");
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
