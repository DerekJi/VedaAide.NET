namespace Veda.Services.DataSources;

/// <summary>
/// 文件类型辅助：判断扩展名是否为二进制文件（图片 / PDF），并提供 MIME 类型映射。
/// </summary>
internal static class FileTypeHelper
{
    private static readonly Dictionary<string, string> MimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = "application/pdf",
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"]  = "image/png",
        [".webp"] = "image/webp",
        [".tiff"] = "image/tiff",
        [".tif"]  = "image/tiff",
        [".bmp"]  = "image/bmp"
    };

    /// <summary>是否为需要走 <see cref="IDocumentIngestor.IngestFileAsync"/> 的二进制文件。</summary>
    public static bool IsBinary(string extension) =>
        MimeMap.ContainsKey(extension);

    /// <summary>获取扩展名对应的 MIME 类型；未知时返回 application/octet-stream。</summary>
    public static string GetMimeType(string extension) =>
        MimeMap.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";
}
