namespace Veda.Core;

/// <summary>
/// Result of a document ingestion operation, containing the DocumentId the caller needs (for later deletion).
/// </summary>
public record IngestResult(string DocumentId, string DocumentName, int ChunksStored);
