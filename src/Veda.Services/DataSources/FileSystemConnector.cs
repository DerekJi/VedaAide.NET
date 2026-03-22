using System.Security.Cryptography;
using System.Text;

namespace Veda.Services.DataSources;

/// <summary>
/// MCP Client 实现之一：从本地文件系统目录批量摄取文档到 VedaAide 知识库。
/// 配置节：<c>Veda:DataSources:FileSystem</c>
/// 通过 <see cref="ISyncStateStore"/> 跟踪内容哈希，跳过内容未变更的文件。
/// </summary>
public sealed class FileSystemConnector(
    IDocumentIngestor                       documentIngestor,
    ISyncStateStore                         syncStateStore,
    IOptions<FileSystemConnectorOptions>    options,
    ILogger<FileSystemConnector>            logger) : IDataSourceConnector
{
    private static readonly string[] DefaultExtensions = [".txt", ".md"];

    public string Name        => "FileSystem";
    public string Description => options.Value.Enabled
        ? $"Syncs files from: {options.Value.Path}"
        : "File system connector (disabled)";
    public bool Enabled => options.Value.Enabled;

    public async Task<DataSourceSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var opts = options.Value;

        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.Path))
        {
            logger.LogInformation("FileSystemConnector: skipped (disabled or path not configured)");
            return MakeResult(0, 0, []);
        }

        if (!Directory.Exists(opts.Path))
        {
            logger.LogWarning("FileSystemConnector: directory not found: {Path}", opts.Path);
            return MakeResult(0, 0, [$"Directory not found: {opts.Path}"]);
        }

        var extensions = opts.Extensions.Length > 0 ? opts.Extensions : DefaultExtensions;

        var files = extensions
            .SelectMany(ext => Directory.GetFiles(opts.Path, $"*{ext}", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var filesProcessed = 0;
        var filesSkipped   = 0;
        var chunksStored   = 0;
        var errors         = new List<string>();

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var content     = await File.ReadAllTextAsync(filePath, ct);
                var contentHash = ComputeHash(content);

                // Skip if file content hasn't changed since last sync
                var knownHash = await syncStateStore.GetContentHashAsync(Name, filePath, ct);
                if (knownHash == contentHash)
                {
                    filesSkipped++;
                    logger.LogDebug("FileSystemConnector: skipping unchanged file '{File}'", filePath);
                    continue;
                }

                var documentName = Path.GetFileName(filePath);
                var docType      = DocumentTypeParser.InferFromName(documentName);

                logger.LogDebug("FileSystemConnector: ingesting '{File}' as {Type}", documentName, docType);

                var result = await documentIngestor.IngestAsync(content, documentName, docType, ct);
                await syncStateStore.UpsertAsync(Name, filePath, contentHash, result.DocumentId, ct);

                filesProcessed++;
                chunksStored += result.ChunksStored;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var msg = $"Failed to ingest '{Path.GetFileName(filePath)}': {ex.Message}";
                errors.Add(msg);
                logger.LogError(ex, "FileSystemConnector: {Error}", msg);
            }
        }

        logger.LogInformation(
            "FileSystemConnector: sync complete — {Files} ingested, {Skipped} unchanged, {Chunks} chunks, {Errors} errors",
            filesProcessed, filesSkipped, chunksStored, errors.Count);

        return MakeResult(filesProcessed, chunksStored, errors);
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static DataSourceSyncResult MakeResult(
        int files, int chunks, IReadOnlyList<string> errors) => new()
    {
        ConnectorName  = "FileSystem",
        FilesProcessed = files,
        ChunksStored   = chunks,
        Errors         = errors,
        SyncedAt       = DateTimeOffset.UtcNow
    };
}
