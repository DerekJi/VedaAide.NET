namespace Veda.Core.Options;

/// <summary>
/// BlobStorageConnector configuration section: <c>Veda:DataSources:BlobStorage</c>
/// </summary>
public sealed class BlobStorageConnectorOptions
{
    /// <summary>Whether to enable this connector. Defaults to false; must be explicitly enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Azure Storage connection string (with SAS or AccountKey). Use either this or AccountUrl.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Storage account URL (e.g. https://myaccount.blob.core.windows.net); set when using DefaultAzureCredential.</summary>
    public string? AccountUrl { get; set; }

    /// <summary>Target blob container name; required.</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>Only sync blobs whose names start with this prefix; leave empty to sync the entire container.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Allowed file extensions (including the dot); leave empty to use the defaults .txt / .md.</summary>
    public string[] Extensions { get; set; } = [".txt", ".md"];
}
