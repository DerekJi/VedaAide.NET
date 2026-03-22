using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Identity;
using System.Security.Cryptography;
using System.Text;

namespace Veda.Services.DataSources;

/// <summary>
/// MCP Client 实现之二：从 Azure Blob Storage 容器批量摄取文档到 VedaAide 知识库。
/// 配置节：<c>Veda:DataSources:BlobStorage</c>
/// 认证：优先 ConnectionString，其次 AccountUrl + DefaultAzureCredential（Managed Identity / 本地 az login）。
/// 通过 <see cref="ISyncStateStore"/> 跟踪内容哈希，跳过内容未变更的 Blob。
/// </summary>
public sealed class BlobStorageConnector(
    IDocumentIngestor                           documentIngestor,
    ISyncStateStore                             syncStateStore,
    IOptions<BlobStorageConnectorOptions>       options,
    ILogger<BlobStorageConnector>               logger) : IDataSourceConnector
{
    private static readonly string[] DefaultExtensions = [".txt", ".md"];

    public string Name        => "BlobStorage";
    public string Description => options.Value.Enabled
        ? $"Syncs blobs from: {options.Value.ContainerName}"
        : "Azure Blob Storage connector (disabled)";
    public bool Enabled => options.Value.Enabled;

    public async Task<DataSourceSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var opts = options.Value;

        if (!opts.Enabled)
        {
            logger.LogInformation("BlobStorageConnector: skipped (disabled)");
            return MakeResult(0, 0, []);
        }

        if (string.IsNullOrWhiteSpace(opts.ContainerName))
        {
            logger.LogWarning("BlobStorageConnector: ContainerName is not configured");
            return MakeResult(0, 0, ["ContainerName is not configured"]);
        }

        BlobContainerClient container;
        try
        {
            container = BuildContainerClient(opts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "BlobStorageConnector: failed to build container client");
            return MakeResult(0, 0, [$"Failed to connect to Azure Blob Storage: {ex.Message}"]);
        }

        var extensions = opts.Extensions.Length > 0 ? opts.Extensions : DefaultExtensions;

        var filesProcessed = 0;
        var filesSkipped   = 0;
        var chunksStored   = 0;
        var errors         = new List<string>();

        await foreach (var blobItem in container.GetBlobsAsync(
            traits: BlobTraits.None, states: BlobStates.None,
            prefix: string.IsNullOrEmpty(opts.Prefix) ? null : opts.Prefix,
            cancellationToken: ct))
        {
            var blobName = blobItem.Name;
            var ext      = Path.GetExtension(blobName);

            if (!extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                continue;

            ct.ThrowIfCancellationRequested();

            try
            {
                var blobClient = container.GetBlobClient(blobName);
                var response   = await blobClient.DownloadContentAsync(ct);
                var content    = response.Value.Content.ToString();

                if (string.IsNullOrWhiteSpace(content))
                {
                    logger.LogDebug("BlobStorageConnector: skipping empty blob '{Blob}'", blobName);
                    continue;
                }

                var contentHash = ComputeHash(content);

                // Skip if blob content hasn't changed since last sync
                var knownHash = await syncStateStore.GetContentHashAsync(Name, blobName, ct);
                if (knownHash == contentHash)
                {
                    filesSkipped++;
                    logger.LogDebug("BlobStorageConnector: skipping unchanged blob '{Blob}'", blobName);
                    continue;
                }

                var documentName = Path.GetFileName(blobName);
                var docType      = DocumentTypeParser.InferFromName(documentName);

                logger.LogDebug("BlobStorageConnector: ingesting '{Blob}' as {Type}", blobName, docType);

                var result = await documentIngestor.IngestAsync(content, documentName, docType, ct);
                await syncStateStore.UpsertAsync(Name, blobName, contentHash, result.DocumentId, ct);

                filesProcessed++;
                chunksStored += result.ChunksStored;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var msg = $"Failed to ingest blob '{blobName}': {ex.Message}";
                errors.Add(msg);
                logger.LogError(ex, "BlobStorageConnector: {Error}", msg);
            }
        }

        logger.LogInformation(
            "BlobStorageConnector: sync complete — {Files} ingested, {Skipped} unchanged, {Chunks} chunks, {Errors} errors",
            filesProcessed, filesSkipped, chunksStored, errors.Count);

        return MakeResult(filesProcessed, chunksStored, errors);
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static BlobContainerClient BuildContainerClient(BlobStorageConnectorOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
            return new BlobContainerClient(opts.ConnectionString, opts.ContainerName);

        if (!string.IsNullOrWhiteSpace(opts.AccountUrl))
        {
            var serviceUri = new Uri(opts.AccountUrl.TrimEnd('/'));
            var service    = new BlobServiceClient(serviceUri, new DefaultAzureCredential());
            return service.GetBlobContainerClient(opts.ContainerName);
        }

        throw new InvalidOperationException(
            "BlobStorageConnector requires either ConnectionString or AccountUrl to be configured.");
    }

    private static DataSourceSyncResult MakeResult(
        int files, int chunks, IReadOnlyList<string> errors) => new()
    {
        ConnectorName  = "BlobStorage",
        FilesProcessed = files,
        ChunksStored   = chunks,
        Errors         = errors,
        SyncedAt       = DateTimeOffset.UtcNow
    };
}
