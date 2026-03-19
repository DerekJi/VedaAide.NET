namespace Veda.Services;

/// <summary>
/// 文档摄取服务（SRP：只负责摄取流程）。
/// 依赖：IDocumentProcessor、IEmbeddingService、IVectorStore。
/// </summary>
public sealed class DocumentIngestService(
    IDocumentProcessor processor,
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    ILogger<DocumentIngestService> logger) : IDocumentIngestor
{
    public async Task<IngestResult> IngestAsync(
        string content,
        string documentName,
        DocumentType documentType,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        // documentId 由 Service 层生成并传入，确保调用方能拿到。
        var documentId = Guid.NewGuid().ToString();
        logger.LogInformation("Ingesting '{Name}' (id={Id}) as {Type}", documentName, documentId, documentType);

        var chunks = processor.Process(content, documentName, documentType, documentId);
        logger.LogInformation("Split '{Name}' into {Count} chunks", documentName, chunks.Count);

        var texts = chunks.Select(c => c.Content).ToList();
        var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts, ct);

        for (var i = 0; i < chunks.Count; i++)
            chunks[i].Embedding = embeddings[i];

        await vectorStore.UpsertBatchAsync(chunks, ct);

        logger.LogInformation("Stored {Count} chunks for '{Name}'", chunks.Count, documentName);
        return new IngestResult(documentId, documentName, chunks.Count);
    }
}
