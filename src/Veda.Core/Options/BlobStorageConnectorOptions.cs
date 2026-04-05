namespace Veda.Core.Options;

/// <summary>
/// BlobStorageConnector 配置节：<c>Veda:DataSources:BlobStorage</c>
/// </summary>
public sealed class BlobStorageConnectorOptions
{
    /// <summary>是否启用此连接器。默认 false，须显式开启。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Azure Storage 连接字符串（含 SAS 或 AccountKey）。与 AccountUrl 二选一。</summary>
    public string? ConnectionString { get; set; }

    /// <summary>存储账户 URL（如 https://myaccount.blob.core.windows.net），使用 DefaultAzureCredential 时填写。</summary>
    public string? AccountUrl { get; set; }

    /// <summary>目标 Blob 容器名称，必填。</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>只同步名称以此前缀开头的 Blob，留空表示同步整个容器。</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>允许的文件扩展名（含点），留空表示使用默认值 .txt / .md。</summary>
    public string[] Extensions { get; set; } = [".txt", ".md"];
}
