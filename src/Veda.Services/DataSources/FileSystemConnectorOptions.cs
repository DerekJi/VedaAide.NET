namespace Veda.Services.DataSources;

public sealed class FileSystemConnectorOptions
{
    public bool     Enabled    { get; set; } = false;
    public string   Path       { get; set; } = string.Empty;
    public string[] Extensions { get; set; } = [".txt", ".md"];
}
