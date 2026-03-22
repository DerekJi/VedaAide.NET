namespace Veda.Core.Interfaces;

/// <summary>
/// 数据源同步状态存储接口。
/// 记录每个连接器已成功同步的文件及其内容哈希，
/// 使得下次 Sync 时可跳过内容未变更的文件。
/// </summary>
public interface ISyncStateStore
{
    /// <summary>
    /// 查询指定连接器下某个文件上次同步时的内容哈希。
    /// 返回 null 表示该文件从未同步过。
    /// </summary>
    Task<string?> GetContentHashAsync(string connectorName, string filePath, CancellationToken ct = default);

    /// <summary>
    /// 写入或更新指定文件的同步状态（内容哈希 + DocumentId）。
    /// </summary>
    Task UpsertAsync(string connectorName, string filePath, string contentHash, string documentId, CancellationToken ct = default);
}
