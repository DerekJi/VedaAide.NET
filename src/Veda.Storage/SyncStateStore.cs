using System.Security.Cryptography;
using System.Text;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// Data source sync-state store implemented with EF Core + SQLite.
/// Records the content hash of every file synced by each connector so unchanged files can be skipped on the next Sync.
/// </summary>
public sealed class SyncStateStore(VedaDbContext db) : ISyncStateStore
{
    public async Task<string?> GetContentHashAsync(
        string connectorName, string filePath, CancellationToken ct = default)
    {
        var record = await db.SyncedFiles
            .AsNoTracking()
            .Where(x => x.ConnectorName == connectorName && x.FilePath == filePath)
            .Select(x => x.ContentHash)
            .FirstOrDefaultAsync(ct);

        return record;
    }

    public async Task UpsertAsync(
        string connectorName, string filePath, string contentHash, string documentId,
        CancellationToken ct = default)
    {
        var existing = await db.SyncedFiles
            .Where(x => x.ConnectorName == connectorName && x.FilePath == filePath)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            db.SyncedFiles.Add(new SyncedFileEntity
            {
                ConnectorName = connectorName,
                FilePath      = filePath,
                ContentHash   = contentHash,
                DocumentId    = documentId,
                SyncedAt      = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.ContentHash = contentHash;
            existing.DocumentId  = documentId;
            existing.SyncedAt    = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a string (lowercase hex, consistent with SqliteVectorStore).
    /// </summary>
    public static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
