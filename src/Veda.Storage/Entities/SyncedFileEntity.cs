namespace Veda.Storage.Entities;

/// <summary>
/// Records the state of every successfully synced file so unchanged files can be skipped on the next Sync.
/// Unique key: (ConnectorName, FilePath); content changes are detected via ContentHash.
/// </summary>
public class SyncedFileEntity
{
    public int    Id            { get; set; }

    /// <summary>Connector name, e.g. "FileSystem" or "BlobStorage".</summary>
    public string ConnectorName { get; set; } = string.Empty;

    /// <summary>File path (absolute path for FileSystem, blob name for BlobStorage).</summary>
    public string FilePath      { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the file content (lowercase hex). A different hash on content change triggers re-ingestion.</summary>
    public string ContentHash   { get; set; } = string.Empty;

    /// <summary>DocumentId produced by this ingestion, for future correlation.</summary>
    public string DocumentId    { get; set; } = string.Empty;

    /// <summary>Timestamp of the last successful sync.</summary>
    public DateTimeOffset SyncedAt { get; set; }
}
