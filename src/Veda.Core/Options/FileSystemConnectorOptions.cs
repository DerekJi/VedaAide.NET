namespace Veda.Core.Options;

/// <summary>
/// FileSystemConnector 配置节：<c>Veda:DataSources:FileSystem</c>
/// </summary>
public sealed class FileSystemConnectorOptions
{
    /// <summary>是否启用此连接器。默认 false，须显式开启。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>本地文件系统路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>允许的文件扩展名（含点），留空表示使用默认值 .txt / .md。</summary>
    public string[] Extensions { get; set; } = [".txt", ".md"];
}
