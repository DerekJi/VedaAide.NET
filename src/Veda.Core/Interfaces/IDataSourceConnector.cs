namespace Veda.Core.Interfaces;

/// <summary>
/// 外部数据源接入契约。实现此接口可将任意外部存储（本地文件系统、Azure Blob 等）
/// 的文档批量摄取到 VedaAide 知识库，作为 MCP Client 的数据端。
/// </summary>
public interface IDataSourceConnector
{
    string Name        { get; }
    string Description { get; }
    bool   Enabled     { get; }

    Task<DataSourceSyncResult> SyncAsync(CancellationToken ct = default);
}

public record DataSourceSyncResult
{
    public required string          ConnectorName  { get; init; }
    public int                      FilesProcessed { get; init; }
    public int                      ChunksStored   { get; init; }
    public IReadOnlyList<string>    Errors         { get; init; } = [];
    public DateTimeOffset           SyncedAt       { get; init; } = DateTimeOffset.UtcNow;
}
