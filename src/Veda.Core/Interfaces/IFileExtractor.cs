namespace Veda.Core.Interfaces;

/// <summary>
/// File content extraction contract: extracts plain text consumable by the RAG pipeline from binary file streams (images / PDFs).
/// The routing strategy is decided by <see cref="DocumentIngestService"/>:
///   - <see cref="DocumentType.RichMedia"/> → Vision model (GPT-4o-mini)
///   - All other types → Azure AI Document Intelligence
/// </summary>
public interface IFileExtractor
{
    /// <summary>
    /// Extracts text content from a file stream.
    /// </summary>
    /// <param name="fileStream">Image or PDF file stream (read-only).</param>
    /// <param name="fileName">Original file name, used for logging and error diagnostics.</param>
    /// <param name="mimeType">MIME type (e.g. image/jpeg, application/pdf).</param>
    /// <param name="documentType">Document type, used to select the extraction strategy (e.g. prebuilt-invoice).</param>
    Task<string> ExtractAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        CancellationToken ct = default);
}
