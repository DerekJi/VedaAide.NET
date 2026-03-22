using System.Security.Cryptography;
using System.Text;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// EF Core + SQLite 实现的数据源同步状态仓库。
/// 记录每个连接器已同步文件的内容哈希，供下次 Sync 时比对，跳过未变更文件。
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
    /// 计算字符串内容的 SHA-256 哈希（小写十六进制，与 SqliteVectorStore 保持一致）。
    /// </summary>
    public static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
