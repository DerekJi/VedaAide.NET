namespace Veda.Core.Interfaces;

/// <summary>
/// Data source sync state storage interface.
/// Records the files each connector has successfully synced along with their content hashes,
/// so the next sync can skip files whose content has not changed.
/// </summary>
public interface ISyncStateStore
{
    /// <summary>
    /// Queries the content hash of a file under the specified connector from its last sync.
    /// Returns null if the file has never been synced.
    /// </summary>
    Task<string?> GetContentHashAsync(string connectorName, string filePath, CancellationToken ct = default);

    /// <summary>
    /// Writes or updates the sync state for the specified file (content hash + DocumentId).
    /// </summary>
    Task UpsertAsync(string connectorName, string filePath, string contentHash, string documentId, CancellationToken ct = default);
}
