using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Veda.Services.DataSources;

namespace Veda.Services;

/// <summary>
/// 演示文档库：从 Blob Storage demo-documents/ 前缀读取预置示例文档，
/// 支持一键 ingest 给招聘方体验问答效果（零上传摩擦）。
/// 未配置 Blob Storage 时返回空列表（优雅降级）。
/// </summary>
public sealed class DemoLibraryService(
    IDocumentIngestor                     documentIngestor,
    IOptions<BlobStorageConnectorOptions> options,
    ILogger<DemoLibraryService>           logger) : IDemoLibraryService
{
    private const string DemoPrefix = "demo-documents/";

    public async Task<IReadOnlyList<DemoDocument>> ListAsync(CancellationToken ct = default)
    {
        var container = TryBuildClient();
        if (container is null) return [];

        try
        {
            var results = new List<DemoDocument>();
            await foreach (var blob in container.GetBlobsAsync(
                traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: DemoPrefix,
                cancellationToken: ct))
            {
                var name = blob.Name[DemoPrefix.Length..];
                if (string.IsNullOrWhiteSpace(name)) continue;

                results.Add(new DemoDocument(
                    Name:        name,
                    Description: blob.Metadata.TryGetValue("description", out var d) ? d : string.Empty,
                    SizeBytes:   blob.Properties.ContentLength ?? 0,
                    Extension:   Path.GetExtension(name).TrimStart('.')));
            }
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DemoLibraryService: failed to list demo documents");
            return [];
        }
    }

    public async Task<IngestResult> IngestAsync(string blobName, CancellationToken ct = default)
    {
        var container = TryBuildClient()
            ?? throw new InvalidOperationException("Blob Storage is not configured.");

        var blobPath = $"{DemoPrefix}{blobName}";
        var blobClient = container.GetBlobClient(blobPath);

        var ext      = Path.GetExtension(blobName).ToLowerInvariant();
        var mimeType = ext switch
        {
            ".pdf"  => "application/pdf",
            ".png"  => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _       => "text/plain"
        };

        logger.LogInformation("DemoLibraryService: ingesting '{Blob}'", blobPath);

        if (mimeType == "text/plain")
        {
            var response = await blobClient.DownloadContentAsync(ct);
            var content  = response.Value.Content.ToString();
            return await documentIngestor.IngestAsync(content, blobName, DocumentType.Other, ct);
        }
        else
        {
            var response = await blobClient.OpenReadAsync(cancellationToken: ct);
            return await documentIngestor.IngestFileAsync(response, blobName, mimeType, DocumentType.Other, ct);
        }
    }

    private BlobContainerClient? TryBuildClient()
    {
        var opts = options.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.ContainerName)) return null;

        try
        {
            if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
                return new BlobContainerClient(opts.ConnectionString, opts.ContainerName);

            if (!string.IsNullOrWhiteSpace(opts.AccountUrl))
            {
                var service = new BlobServiceClient(new Uri(opts.AccountUrl.TrimEnd('/')), new DefaultAzureCredential());
                return service.GetBlobContainerClient(opts.ContainerName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DemoLibraryService: failed to build Blob client");
        }
        return null;
    }
}
