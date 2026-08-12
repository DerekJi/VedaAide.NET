namespace Veda.Core.Interfaces;

/// <summary>
/// Contract for connecting to external data sources. Implementing this interface allows batch ingestion of documents
/// from any external storage (local file system, Azure Blob, etc.) into the VedaAide knowledge base, serving as the data side of the MCP Client.
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
