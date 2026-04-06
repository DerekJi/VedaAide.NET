using Veda.Core.Options;
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

        // 语义增强：为每个 chunk 生成并追加语义元数据（别名标签、检测到的术语等）
        // 这确保摄入时的语义标注与检索时的查询扩展逻辑保持对齐
        foreach (var chunk in chunks)
        {
            chunk.Version = version;
            // 写入 OwnerId scope，确保文档按用户隔离
            if (scope?.OwnerId is not null)
                chunk.Metadata["_scope_ownerId"] = scope.OwnerId;

            // 通过 GetEnhancedMetadataAsync 同时应用 Vocabulary 和 Tags 规则
            var enhancement = await semanticEnhancer.GetEnhancedMetadataAsync(chunk.Content, ct);

            // 写入别名标签
            if (enhancement.AliasTags.Count > 0)
                chunk.Metadata["aliasTags"] = string.Join(",", enhancement.AliasTags);

            // 写入检测到的术语和同义词（JSON 格式便于后续检索端使用）
            if (enhancement.DetectedTermsWithSynonyms.Count > 0)
            {
                var termDict = enhancement.DetectedTermsWithSynonyms.ToDictionary(
                    kv => kv.Key,
                    kv => (object)kv.Value.ToList()
                );
                chunk.Metadata["detectedTerms"] = System.Text.Json.JsonSerializer.Serialize(termDict);
            }
        }

        var texts = chunks.Select(c => c.Content).ToList();
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts, ct);

        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].Embedding = embeddings[i];
            chunks[i].EmbeddingModel = vedaOptions.Value.EmbeddingModel;
        }

        // 第二层去重：过滤与已存储内容向量相似度过高的块（语义近似重复）。
        // Certificate 类型使用更低阈值（0.70），避免内容结构相近的证书互相误杀。
        var dedupThreshold = ChunkingOptions.ForDocumentType(documentType).DedupThreshold;
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
        // 仅当有新 chunks 需要写入时才执行 supersede：若所有块均被去重跳过，
        // 保留原有 chunks 不变，避免文档从列表中消失。
        if (oldDocumentId is not null && deduped.Count > 0)
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

        // PDF 文字层直通：纯文字 PDF 跳过 OCR 管线。
        // Certificate 类型跳过 PdfPig（表格/排版复杂，GetWords 词序混乱），直走 Azure DI。
        if (mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            && documentType != DocumentType.Certificate)
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
        catch (Exception ex) when (!ReferenceEquals(extractor, visionExtractor))
        {
            var reason = ex is QuotaExceededException ? "quota exceeded" : $"{ex.GetType().Name}: {ex.Message}";
            logger.LogWarning(ex, "Azure DI failed ({Reason}), falling back to Vision model for '{Name}'",
                reason, fileName);
            buffered.Position = 0;
            try
            {
                extractedText = await visionExtractor.ExtractAsync(buffered, fileName, mimeType, documentType, ct);
            }
            catch (Exception vex)
            {
                // Vision not enabled, not configured, or the chat model doesn't support images.
                logger.LogWarning(vex,
                    "Vision extraction failed for '{Name}' ({ExType}); returning 0 chunks",
                    fileName, vex.GetType().Name);
                return new IngestResult(Guid.NewGuid().ToString(), fileName, 0);
            }
        }

        // Guard: DI or Vision may return empty text (blank/corrupted document)
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            logger.LogWarning(
                "Extractor returned empty text for '{Name}'; returning 0 chunks", fileName);
            return new IngestResult(Guid.NewGuid().ToString(), fileName, 0);
        }

        return await IngestAsync(extractedText, fileName, documentType, scope, ct);
    }
}
