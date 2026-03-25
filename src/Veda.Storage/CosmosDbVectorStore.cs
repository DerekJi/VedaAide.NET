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
        CancellationToken ct = default)
    {
        var candidateCount = Math.Max(topK * CandidateMultiplier, 20);
        var vecJson = VectorToJson(queryEmbedding);

        // Build optional WHERE conditions for scalar filters
        var conditions = new List<string>();
        if (filterType.HasValue) conditions.Add("c.documentType = @filterType");
        if (dateFrom.HasValue)   conditions.Add("c.createdAtTicks >= @dateFrom");
        if (dateTo.HasValue)     conditions.Add("c.createdAtTicks <= @dateTo");

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
        CreatedAtTicks = chunk.CreatedAt.UtcTicks
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
        CreatedAt      = new DateTimeOffset(r.CreatedAtTicks, TimeSpan.Zero)
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
}
