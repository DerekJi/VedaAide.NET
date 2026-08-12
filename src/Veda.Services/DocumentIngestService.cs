using Veda.Core.Options;
namespace Veda.Services;

/// <summary>
/// Document ingestion service (SRP: only responsible for the ingestion pipeline).
/// Dependencies: IDocumentProcessor, IEmbeddingService, IVectorStore, IFileExtractor (two implementations).
/// Text-layer PDF pass-through extraction: PdfTextLayerExtractor first, scanned documents fall back to Azure DI.
/// Falls back to the Vision model automatically when the Azure DI quota is exceeded (QuotaExceededException fallback).
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

        // Versioning: check whether a document with the same name already exists
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

        // Semantic enhancement: generate and append semantic metadata for each chunk (alias tags, detected terms, etc.)
        // This keeps the semantic annotations at ingest time aligned with the query expansion logic at retrieval time
        foreach (var chunk in chunks)
        {
            chunk.Version = version;
            // Write the OwnerId scope to keep documents isolated per user
            if (scope?.OwnerId is not null)
                chunk.Metadata["_scope_ownerId"] = scope.OwnerId;

            // Apply both Vocabulary and Tags rules via GetEnhancedMetadataAsync
            var enhancement = await semanticEnhancer.GetEnhancedMetadataAsync(chunk.Content, ct);

            // Write alias tags
            if (enhancement.AliasTags.Count > 0)
                chunk.Metadata["aliasTags"] = string.Join(",", enhancement.AliasTags);

            // Write detected terms and synonyms (JSON format so the retrieval side can use them later)
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

        // Second-layer dedup: filter out chunks whose vectors are too similar to already stored content (semantic near-duplicates).
        // The Certificate type uses a lower threshold (0.70) to avoid falsely eliminating structurally similar certificates.
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

        // Versioning: mark the old-version chunks as superseded first, then write the new chunks.
        // Order matters — mark first, write later: if UpsertBatch ran first, the WHERE SupersededAtTicks==0
        // would also match the freshly written chunks and immediately mark them superseded.
        // Only supersede when there are new chunks to write: if every chunk is skipped by dedup,
        // keep the existing chunks untouched so the document does not disappear from the listing.
        if (oldDocumentId is not null && deduped.Count > 0)
            await vectorStore.MarkDocumentSupersededAsync(documentName, documentId, ct);

        if (deduped.Count > 0)
            await vectorStore.UpsertBatchAsync(deduped, ct);

        logger.LogInformation(
            "Stored {Stored}/{Total} chunks for '{Name}' v{Version} (skipped {Skipped} near-duplicates)",
            deduped.Count, chunks.Count, documentName, version, chunks.Count - deduped.Count);

        // Clear the semantic cache after knowledge base content changes to avoid returning stale answers (async, does not block the response).
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

        // Buffer the fileStream: allows handing the same stream to the Vision fallback when the Azure DI quota is exceeded
        using var buffered = new MemoryStream();
        await fileStream.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        string extractedText;

        // PDF text-layer pass-through: plain-text PDFs skip the OCR pipeline.
        // The Certificate type skips PdfPig (complex tables/layout, GetWords word order is scrambled) and goes straight to Azure DI.
        if (mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            && documentType != DocumentType.Certificate)
        {
            var textLayerResult = pdfTextLayerExtractor.TryExtract(buffered, fileName);
            if (textLayerResult is not null)
                return await IngestAsync(textLayerResult, fileName, documentType, scope, ct);

            // Scanned copy (empty text layer): reset the stream and fall back to Azure DI
            logger.LogInformation(
                "PdfTextLayerExtractor: '{Name}' is a scanned PDF, falling back to Document Intelligence",
                fileName);
            buffered.Position = 0;
        }

        // Routing: RichMedia → Vision model; everything else → Document Intelligence
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
