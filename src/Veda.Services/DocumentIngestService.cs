namespace Veda.Services;

/// <summary>
/// 文档摄取服务（SRP：只负责摄取流程）。
/// 依赖：IDocumentProcessor、IEmbeddingService、IVectorStore、IFileExtractor（两个实现）。
/// 文字层 PDF 直通提取：PdfTextLayerExtractor 优先，扫描件降级到 Azure DI。
/// Azure DI 配额超限时自动降级到 Vision 模型（QuotaExceededException fallback）。
/// </summary>
public sealed class DocumentIngestService(
    IDocumentProcessor processor,
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    ISemanticCache semanticCache,
    ISemanticEnhancer semanticEnhancer,
    IDocumentDiffService documentDiffService,
    IOptions<RagOptions> options,
    IOptions<VedaOptions> vedaOptions,
    DocumentIntelligenceFileExtractor docIntelExtractor,
    VisionModelFileExtractor visionExtractor,
    PdfTextLayerExtractor pdfTextLayerExtractor,
    ILogger<DocumentIngestService> logger) : IDocumentIngestor
{
    private const int LogSnippetLength = 50;

    public async Task<IngestResult> IngestAsync(
        string content,
        string documentName,
        DocumentType documentType,
        KnowledgeScope? scope = null,
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
            // 写入 OwnerId scope，确保文档按用户隔离
            if (scope?.OwnerId is not null)
                chunk.Metadata["_scope_ownerId"] = scope.OwnerId;
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

        // 版本化：先标记旧版本 chunks 为已取代，再写入新 chunks。
        // 顺序必须先标记后写入：若先 UpsertBatch 再标记，则 WHERE SupersededAtTicks==0
        // 会同时命中刚写入的新 chunk，导致新 chunk 被立刻标记为已取代。
        if (oldDocumentId is not null)
            await vectorStore.MarkDocumentSupersededAsync(documentName, documentId, ct);

        if (deduped.Count > 0)
            await vectorStore.UpsertBatchAsync(deduped, ct);

        logger.LogInformation(
            "Stored {Stored}/{Total} chunks for '{Name}' v{Version} (skipped {Skipped} near-duplicates)",
            deduped.Count, chunks.Count, documentName, version, chunks.Count - deduped.Count);

        // 知识库内容变更后清空语义缓存，避免返回过期答案（异步，不阻塞响应）。
        _ = semanticCache.ClearAsync(CancellationToken.None);

        return new IngestResult(documentId, documentName, deduped.Count);
    }

    public async Task<IngestResult> IngestFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        KnowledgeScope? scope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        // 缓冲 fileStream：允许 Azure DI 配额超限时将同一流交给 Vision 降级处理
        using var buffered = new MemoryStream();
        await fileStream.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        string extractedText;

        // PDF 文字层直通：纯文字 PDF 跳过 OCR 管线
        if (mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            var textLayerResult = pdfTextLayerExtractor.TryExtract(buffered, fileName);
            if (textLayerResult is not null)
                return await IngestAsync(textLayerResult, fileName, documentType, scope, ct);

            // 打印件（文字层为空）：重置流并降级到 Azure DI
            logger.LogInformation(
                "PdfTextLayerExtractor: '{Name}' is a scanned PDF, falling back to Document Intelligence",
                fileName);
            buffered.Position = 0;
        }

        // 路由：RichMedia → Vision 模型；其余 → Document Intelligence
        IFileExtractor extractor = documentType == DocumentType.RichMedia
            ? visionExtractor
            : docIntelExtractor;

        logger.LogInformation(
            "File ingestion '{Name}' ({MimeType}) as {Type} via {Extractor}",
            fileName, mimeType, documentType, extractor.GetType().Name);

        try
        {
            extractedText = await extractor.ExtractAsync(buffered, fileName, mimeType, documentType, ct);
        }
        catch (QuotaExceededException) when (!ReferenceEquals(extractor, visionExtractor))
        {
            logger.LogWarning(
                "Azure DI quota exceeded, falling back to Vision model for '{Name}'", fileName);
            buffered.Position = 0;
            extractedText = await visionExtractor.ExtractAsync(buffered, fileName, mimeType, documentType, ct);
        }

return await IngestAsync(extractedText, fileName, documentType, scope, ct);
    }
}
