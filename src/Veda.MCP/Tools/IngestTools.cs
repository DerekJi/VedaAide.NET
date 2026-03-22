namespace Veda.MCP.Tools;

/// <summary>
/// MCP 文档摄取工具。允许通过 LLM 调用直接向知识库摄取文本。
/// </summary>
[McpServerToolType]
public sealed class IngestTools(IDocumentIngestor documentIngestor)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    [McpServerTool(Name = "ingest_document")]
    [Description("Ingest a text document into the VedaAide knowledge base. The content will be chunked, embedded and stored for future retrieval.")]
    public async Task<string> IngestDocument(
        [Description("The full text content to ingest")] string content,
        [Description("A human-readable name for this document (e.g. 'Q4-2025-Report.md')")] string documentName,
        [Description("Document type: BillInvoice, Specification, Report, or Other")] string documentType = "Other",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        var parsedType = DocumentTypeParser.ParseOrDefault(documentType);
        var result = await documentIngestor.IngestAsync(content, documentName, parsedType, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            documentId = result.DocumentId,
            chunksStored = result.ChunksStored,
            documentName = result.DocumentName
        }, SerializerOptions);
    }
}
