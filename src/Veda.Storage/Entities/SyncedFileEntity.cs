namespace Veda.Storage.Entities;

/// <summary>
/// 记录每次成功同步的文件状态，用于在下次 Sync 时跳过未变更的文件。
/// 唯一键：(ConnectorName, FilePath)，通过 ContentHash 检测内容变化。
/// </summary>
public class SyncedFileEntity
{
    public int    Id            { get; set; }

    /// <summary>连接器名称，如 "FileSystem" 或 "BlobStorage"。</summary>
    public string ConnectorName { get; set; } = string.Empty;

    /// <summary>文件路径（FileSystem 为绝对路径，BlobStorage 为 blob name）。</summary>
    public string FilePath      { get; set; } = string.Empty;

    /// <summary>文件内容的 SHA-256 哈希（小写十六进制）。内容変化时哈希不同，触发重新摄取。</summary>
    public string ContentHash   { get; set; } = string.Empty;

    /// <summary>本次摄取产生的 DocumentId，便于将来关联操作。</summary>
    public string DocumentId    { get; set; } = string.Empty;

    /// <summary>最后一次成功同步的时间。</summary>
    public DateTimeOffset SyncedAt { get; set; }
}
