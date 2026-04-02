using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Veda.Services.DataSources;

namespace Veda.Services;

/// <summary>
/// Demo document library service.
/// Primary source: Blob Storage demo-documents/ prefix (cloud or Azurite).
/// Fallback source: local FileSystem path (when Blob Storage is not configured — development mode).
/// Returns an empty list when neither source is available; never throws.
/// </summary>
public sealed class DemoLibraryService(
    IDocumentIngestor                          documentIngestor,
    IOptions<BlobStorageConnectorOptions>      blobOptions,
    IOptions<FileSystemConnectorOptions>       fsOptions,
    ILogger<DemoLibraryService>                logger) : IDemoLibraryService
{
    private const string DemoPrefix = "demo-documents/";

    public async Task<IReadOnlyList<DemoDocument>> ListAsync(CancellationToken ct = default)
    {
        var container = TryBuildClient();
        if (container is not null)
            return await ListFromBlobAsync(container, ct);

        return ListFromFileSystem();
    }

    public async Task<IngestResult> IngestAsync(string documentName, KnowledgeScope? scope = null, DocumentType? documentType = null, CancellationToken ct = default)
    {
        var container = TryBuildClient();
        if (container is not null)
            return await IngestFromBlobAsync(container, documentName, scope, documentType, ct);

        return await IngestFromFileSystemAsync(documentName, scope, documentType, ct);
    }

    // ── Blob Storage source ───────────────────────────────────────────────────

    private async Task<IReadOnlyList<DemoDocument>> ListFromBlobAsync(BlobContainerClient container, CancellationToken ct)
    {
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
            logger.LogWarning(ex, "DemoLibraryService: failed to list demo documents from Blob Storage");
            return [];
        }
    }

    private async Task<IngestResult> IngestFromBlobAsync(
        BlobContainerClient container, string blobName, KnowledgeScope? scope, DocumentType? documentType, CancellationToken ct)
    {
        var blobPath   = $"{DemoPrefix}{blobName}";
        var blobClient = container.GetBlobClient(blobPath);
        var ext        = Path.GetExtension(blobName).ToLowerInvariant();
        var mimeType   = ResolveMimeType(ext);
        var docType    = documentType ?? DocumentTypeParser.InferFromName(blobName);

        logger.LogInformation("DemoLibraryService: ingesting '{Blob}' from Blob Storage as {Type}", blobPath, docType);

        if (mimeType == "text/plain")
        {
            var response = await blobClient.DownloadContentAsync(ct);
            var content  = response.Value.Content.ToString();
            return await documentIngestor.IngestAsync(content, blobName, docType, scope, ct);
        }
        else
        {
            var stream = await blobClient.OpenReadAsync(cancellationToken: ct);
            return await documentIngestor.IngestFileAsync(stream, blobName, mimeType, docType, scope, ct);
        }
    }

    // ── FileSystem fallback source ────────────────────────────────────────────

    private IReadOnlyList<DemoDocument> ListFromFileSystem()
    {
        var opts = fsOptions.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.Path) || !Directory.Exists(opts.Path))
            return [];

        try
        {
            var extensions = new HashSet<string>(
                opts.Extensions.Select(e => e.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            return Directory
                .EnumerateFiles(opts.Path)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => new FileInfo(f))
                .Select(fi => new DemoDocument(
                    Name:        fi.Name,
                    Description: string.Empty,
                    SizeBytes:   fi.Length,
                    Extension:   fi.Extension.TrimStart('.')))
                .OrderBy(d => d.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DemoLibraryService: failed to list demo documents from FileSystem");
            return [];
        }
    }

    private async Task<IngestResult> IngestFromFileSystemAsync(
        string fileName, KnowledgeScope? scope, DocumentType? documentType, CancellationToken ct)
    {
        var opts = fsOptions.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.Path))
            throw new InvalidOperationException("Neither Blob Storage nor FileSystem is configured.");

        var filePath = Path.Combine(opts.Path, fileName);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Demo file not found: {fileName}", filePath);

        var ext      = Path.GetExtension(fileName).ToLowerInvariant();
        var mimeType = ResolveMimeType(ext);
        var docType  = documentType ?? DocumentTypeParser.InferFromName(fileName);

        logger.LogInformation("DemoLibraryService: ingesting '{File}' from FileSystem as {Type}", filePath, docType);

        if (mimeType == "text/plain")
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            return await documentIngestor.IngestAsync(content, fileName, docType, scope, ct);
        }
        else
        {
            var stream = File.OpenRead(filePath);
            await using (stream.ConfigureAwait(false))
                return await documentIngestor.IngestFileAsync(stream, fileName, mimeType, docType, scope, ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private BlobContainerClient? TryBuildClient()
    {
        var opts = blobOptions.Value;
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.ContainerName)) return null;

        try
        {
            if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
                return new BlobContainerClient(opts.ConnectionString, opts.ContainerName);

            if (!string.IsNullOrWhiteSpace(opts.AccountUrl))
            {
                var service = new BlobServiceClient(
                    new Uri(opts.AccountUrl.TrimEnd('/')), new DefaultAzureCredential());
                return service.GetBlobContainerClient(opts.ContainerName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DemoLibraryService: failed to build Blob client");
        }
        return null;
    }

    private static string ResolveMimeType(string ext) => ext switch
    {
        ".pdf"             => "application/pdf",
        ".png"             => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp"            => "image/webp",
        ".tiff" or ".bmp" => "image/tiff",
        _                  => "text/plain"
    };
}
