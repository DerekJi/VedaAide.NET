namespace Veda.Services;

/// <summary>
/// 文档摄取服务（SRP：只负责摄取流程）。
/// 依赖：IDocumentProcessor、IEmbeddingService、IVectorStore、IFileExtractor（两个实现）。
/// </summary>
public sealed class DocumentIngestService(
    IDocumentProcessor processor,
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    ISemanticEnhancer semanticEnhancer,
    IDocumentDiffService documentDiffService,
    IOptions<RagOptions> options,
    IOptions<VedaOptions> vedaOptions,
    DocumentIntelligenceFileExtractor docIntelExtractor,
    VisionModelFileExtractor visionExtractor,
    ILogger<DocumentIngestService> logger) : IDocumentIngestor
{
    private const int LogSnippetLength = 50;

    public async Task<IngestResult> IngestAsync(
        string content,
        string documentName,
        DocumentType documentType,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        // 版本化：检查是否已存在同名文档
        var existingChunks = await vectorStore.GetCurrentChunksByDocumentNameAsync(documentName, ct);
        var version = 1;
        string? oldDocumentId = null;

        if (existingChunks.Count > 0)
        {
            oldDocumentId = existingChunks[0].DocumentId;
            version = existingChunks.Max(c => c.Version) + 1;
            var oldContent = string.Join("\n", existingChunks.OrderBy(c => c.ChunkIndex).Select(c => c.Content));
            var diff = await documentDiffService.DiffAsync(oldDocumentId, oldContent, content, ct);
            logger.LogInformation(
                "Document '{Name}' updated: +{Added} -{Removed} ~{Modified} chunks, topics: {Topics}",
                documentName, diff.AddedChunks, diff.RemovedChunks, diff.ModifiedChunks,
                string.Join(", ", diff.ChangedTopics));
        }

        var documentId = Guid.NewGuid().ToString();
        logger.LogInformation("Ingesting '{Name}' (id={Id}) v{Version} as {Type}",
            documentName, documentId, version, documentType);

        var chunks = processor.Process(content, documentName, documentType, documentId);
        logger.LogInformation("Split '{Name}' into {Count} chunks", documentName, chunks.Count);

        // 语义增强：为每个 chunk 追加别名标签到 metadata
        foreach (var chunk in chunks)
        {
            chunk.Version = version;
            var aliasTags = await semanticEnhancer.GetAliasTagsAsync(chunk.Content, ct);
            if (aliasTags.Count > 0)
                chunk.Metadata["aliasTags"] = string.Join(",", aliasTags);
        }

        var texts = chunks.Select(c => c.Content).ToList();
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts, ct);

        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].Embedding = embeddings[i];
            chunks[i].EmbeddingModel = vedaOptions.Value.EmbeddingModel;
        }

        // 第二层去重：过滤与已存储内容向量相似度过高的块（语义近似重复）。
        var dedupThreshold = options.Value.SimilarityDedupThreshold;
        var deduped = new List<DocumentChunk>();
        foreach (var chunk in chunks)
        {
            var similar = await vectorStore.SearchAsync(
                chunk.Embedding!, topK: 1, minSimilarity: dedupThreshold, ct: ct);
            if (similar.Count == 0)
                deduped.Add(chunk);
            else
                logger.LogDebug(
                    "Skipping near-duplicate chunk (similarity: {Score:P0}): '{Snippet}'",
                    similar[0].Similarity,
                    chunk.Content[..Math.Min(LogSnippetLength, chunk.Content.Length)]);
        }

        if (deduped.Count > 0)
            await vectorStore.UpsertBatchAsync(deduped, ct);

        // 版本化：标记旧版本 chunks 为已取代
        if (oldDocumentId is not null)
            await vectorStore.MarkDocumentSupersededAsync(documentName, documentId, ct);

        logger.LogInformation(
            "Stored {Stored}/{Total} chunks for '{Name}' v{Version} (skipped {Skipped} near-duplicates)",
            deduped.Count, chunks.Count, documentName, version, chunks.Count - deduped.Count);

        return new IngestResult(documentId, documentName, deduped.Count);
    }

    public async Task<IngestResult> IngestFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        // 路由：RichMedia → Vision 模型；其余 → Document Intelligence
        IFileExtractor extractor = documentType == DocumentType.RichMedia
            ? visionExtractor
            : docIntelExtractor;

        logger.LogInformation(
            "File ingestion '{Name}' ({MimeType}) as {Type} via {Extractor}",
            fileName, mimeType, documentType, extractor.GetType().Name);

        var extractedText = await extractor.ExtractAsync(fileStream, fileName, mimeType, documentType, ct);
        return await IngestAsync(extractedText, fileName, documentType, ct);
    }
}
