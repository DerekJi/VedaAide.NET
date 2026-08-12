using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Veda.Services;

/// <summary>
/// Pure text-layer PDF pass-through extractor.
/// Uses PdfPig (MIT license, zero external dependencies) to read the PDF text layer directly,
/// skipping the OCR pipeline.
///
/// Scanned-document detection: if the average characters per page is below <see cref="MinCharsPerPage"/>,
/// the document is treated as scanned and null is returned so the caller can fall back to Azure DI / Vision.
/// </summary>
public sealed class PdfTextLayerExtractor(ILogger<PdfTextLayerExtractor> logger)
{
    /// <summary>
    /// Characters-per-page threshold; below this value the PDF is treated as scanned.
    /// 20 is an empirical value: a true scanned PDF yields 0–5 characters (noise) from the text layer,
    /// while sparse text PDFs (e.g. certificates, single-page notices) may have only 50–100 characters
    /// and should still take the text path. The former value of 100 misclassified sparse-text PDFs like
    /// certificates as scanned, unnecessarily triggering the Vision model.
    /// </summary>
    private const int MinCharsPerPage = 20;

    /// <summary>
    /// Attempts to extract text from the PDF text layer.
    /// </summary>
    /// <returns>
    /// The extracted text (a non-empty string) indicates success;
    /// null means the text layer is empty (scanned document), and the caller should fall back to OCR.
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
                // GetWords() preserves word order more accurately than merging Letters
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
