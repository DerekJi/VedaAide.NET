namespace Veda.Core.Interfaces;

/// <summary>
/// Document ingestion service contract (write operations).
/// ISP: separated from query operations, so a Controller depends only on the interfaces it needs.
/// </summary>
public interface IDocumentIngestor
{
    /// <summary>
    /// Ingests plain-text documents: chunk → embed → deduplicate → store.
    /// </summary>
    /// <returns>A result containing the DocumentId, which callers can use for subsequent deletion.</returns>
    Task<IngestResult> IngestAsync(
        string content,
        string documentName,
        DocumentType documentType,
        KnowledgeScope? scope = null,
        CancellationToken ct = default);

    /// <summary>
    /// Ingests binary files (images / PDFs): file extraction → chunking → embedding → deduplication → storage.
    /// Routing strategy: <see cref="DocumentType.RichMedia"/> uses a Vision model; all other types use Azure AI Document Intelligence.
    /// </summary>
    /// <param name="fileStream">Image or PDF file stream.</param>
    /// <param name="fileName">Original file name (including extension), used for logging and metadata.</param>
    /// <param name="mimeType">MIME type (e.g. image/jpeg, application/pdf).</param>
    /// <param name="documentType">Document type, which determines the extraction model and chunking strategy.</param>
    Task<IngestResult> IngestFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        KnowledgeScope? scope = null,
        CancellationToken ct = default);
}
