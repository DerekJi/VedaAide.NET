namespace Veda.Services.DataSources;

/// <summary>
/// File type helpers: determine whether an extension denotes a binary file (image / PDF) or an email file (.eml / .msg), and provide MIME type mapping.
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

    private static readonly HashSet<string> EmailExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".eml", ".msg" };

    /// <summary>Whether the file is a binary file that must go through <see cref="IDocumentIngestor.IngestFileAsync"/>.</summary>
    public static bool IsBinary(string extension) =>
        MimeMap.ContainsKey(extension);

    /// <summary>Whether the file is an email file that must be parsed by <see cref="EmailTextExtractor"/>.</summary>
    public static bool IsEmail(string extension) =>
        EmailExtensions.Contains(extension);

    /// <summary>Gets the MIME type for an extension; returns application/octet-stream when unknown.</summary>
    public static string GetMimeType(string extension) =>
        MimeMap.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";
}
