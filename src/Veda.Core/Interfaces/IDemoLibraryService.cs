namespace Veda.Core.Interfaces;

/// <summary>Demo library document entry.</summary>
public record DemoDocument(
    string Name,
    string Description,
    long   SizeBytes,
    string Extension);

/// <summary>
/// Demo library service contract: lists available sample documents and supports one-click ingest into the knowledge base.
/// Sources: Blob Storage demo-documents/ prefix (cloud/Azurite), or local FileSystem path (development fallback).
/// </summary>
public interface IDemoLibraryService
{
    /// <summary>Lists all available demo documents from the configured source.</summary>
    Task<IReadOnlyList<DemoDocument>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Ingests the specified demo document into the knowledge base.
    /// When scope is provided the document is stored under that user's partition; otherwise it is public.
    /// When documentType is provided it overrides the auto-detected type.
    /// </summary>
    Task<IngestResult> IngestAsync(string documentName, KnowledgeScope? scope = null, DocumentType? documentType = null, CancellationToken ct = default);
}
