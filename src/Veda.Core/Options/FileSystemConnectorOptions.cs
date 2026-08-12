namespace Veda.Core.Options;

/// <summary>
/// FileSystemConnector configuration section: <c>Veda:DataSources:FileSystem</c>
/// </summary>
public sealed class FileSystemConnectorOptions
{
    /// <summary>Whether to enable this connector. Defaults to false; must be explicitly enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Local file system path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Allowed file extensions (including the dot); leave empty to use the defaults .txt / .md.</summary>
    public string[] Extensions { get; set; } = [".txt", ".md"];
}
