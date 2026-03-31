using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Veda.Services;

/// <summary>
/// 纯文字层 PDF 直通提取器。
/// 使用 PdfPig（MIT 协议，零外部依赖）直接读取 PDF 文字层，跳过 OCR 管线。
///
/// 扫描件检测：若平均每页字符数低于 <see cref="MinCharsPerPage"/>，
/// 判定为扫描件并返回 null，由调用方降级到 Azure DI / Vision。
/// </summary>
public sealed class PdfTextLayerExtractor(ILogger<PdfTextLayerExtractor> logger)
{
    /// <summary>
    /// 每页字符数阈值，低于此值视为扫描件。
    /// 100 为经验值：大多数扫描件在文字层提取时得到 0–20 字符（噪点），
    /// 而文字型 PDF 通常每页 300+ 字符。
    /// </summary>
    private const int MinCharsPerPage = 100;

    /// <summary>
    /// 尝试从 PDF 文字层提取文本。
    /// </summary>
    /// <returns>
    /// 提取的文本（非空字符串）表示成功；
    /// null 表示文字层为空（扫描件），调用方应降级到 OCR。
    /// </returns>
    public string? TryExtract(Stream pdfStream, string fileName)
    {
        try
        {
            using var ms = new MemoryStream();
            pdfStream.CopyTo(ms);
            var bytes = ms.ToArray();

            using var document = PdfDocument.Open(bytes);
            var pages = document.GetPages().ToList();
            if (pages.Count == 0)
                return null;

            var sb = new System.Text.StringBuilder();
            foreach (var page in pages)
            {
                // GetWords() 比 Letters 合并更准确地保留词序
                var words = page.GetWords().Select(w => w.Text);
                sb.AppendLine(string.Join(" ", words));
            }

            var text = sb.ToString().Trim();
            var avgCharsPerPage = text.Length / pages.Count;

            if (avgCharsPerPage < MinCharsPerPage)
            {
                logger.LogInformation(
                    "PdfTextLayerExtractor: '{Name}' averages {Avg} chars/page — identified as scanned PDF, falling back to OCR",
                    fileName, avgCharsPerPage);
                return null;
            }

            logger.LogInformation(
                "PdfTextLayerExtractor: '{Name}' extracted {Chars} chars from {Pages} page(s) ({Avg} avg chars/page)",
                fileName, text.Length, pages.Count, avgCharsPerPage);

            return text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PdfTextLayerExtractor: failed to open '{Name}', will fall back to OCR", fileName);
            return null;
        }
    }
}
