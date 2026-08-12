namespace Veda.Core.Interfaces;

/// <summary>
/// Document processing pipeline: parses raw content and outputs a list of chunks.
/// </summary>
public interface IDocumentProcessor
{
    /// <param name="content">Raw text content</param>
    /// <param name="documentName">File name, used for metadata</param>
    /// <param name="documentType">Affects chunk granularity</param>
    /// <param name="documentId">Document ID passed in by the service layer, ensuring the caller can retain it for deletion and testing</param>
    IReadOnlyList<DocumentChunk> Process(string content, string documentName, DocumentType documentType, string documentId);
}
