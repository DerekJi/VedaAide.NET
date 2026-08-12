using Veda.Core.Options;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using Veda.Core;

namespace Veda.Services;

/// <summary>
/// File content extraction implementation based on Azure AI Document Intelligence.
/// Routing strategy:
///   - BillInvoice → "prebuilt-invoice" (structured invoice/bill extraction)
///   - Other types → "prebuilt-read" (general OCR + layout-aware reading)
/// Outputs <see cref="Azure.AI.FormRecognizer.DocumentAnalysis.AnalyzeResult.Content"/>,
/// the Markdown representation of the full document text, which feeds directly into the existing text chunking pipeline.
///
/// Quota-aware: on a 429 response, marks <see cref="AzureDiQuotaState"/> and throws
/// <see cref="QuotaExceededException"/> so that upper-layer services can catch it and fall back to the Vision model.
/// Subsequent requests in the same month read the in-memory state first and skip the actual Azure DI call.
/// </summary>
public class DocumentIntelligenceFileExtractor(
    IOptions<DocumentIntelligenceOptions> options,
    ILogger<DocumentIntelligenceFileExtractor> logger,
    AzureDiQuotaState quotaState) : IFileExtractor
{
    public async Task<string> ExtractAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        CancellationToken ct = default)
    {
        // Quota-exceeded fast path: throw immediately without consuming the fileStream
        if (quotaState.IsExceeded)
        {
            logger.LogWarning(
                "Azure DI quota exceeded (cached state). Skipping DI call for '{Name}'", fileName);
            throw new QuotaExceededException(
                "Azure AI Document Intelligence monthly quota is exceeded. Falling back to Vision model.");
        }

        if (!options.Value.IsConfigured)
            throw new QuotaExceededException(
                "Azure AI Document Intelligence is not configured. Falling back to Vision model.");

        var modelId = documentType switch
        {
            DocumentType.BillInvoice => "prebuilt-invoice",
            DocumentType.Identity    => "prebuilt-idDocument",
            _                        => "prebuilt-read",
        };

        logger.LogInformation(
            "Extracting '{Name}' ({MimeType}) with Document Intelligence model '{Model}'",
            fileName, mimeType, modelId);

        try
        {
            var content = await CallAzureDiAsync(modelId, fileStream, ct);

            logger.LogInformation(
                "Extracted {Chars} chars from '{Name}' via Document Intelligence",
                content.Length, fileName);

            return content;
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            quotaState.MarkExceeded();
            logger.LogWarning(
                "Azure DI returned HTTP 429 for '{Name}'. Quota marked exceeded until next month.",
                fileName);
            throw new QuotaExceededException(
                "Azure AI Document Intelligence monthly quota is exceeded. Falling back to Vision model.", ex);
        }
    }

    /// <summary>
    /// Isolation point for the actual Azure SDK call. Subclasses can override it to inject different behavior in tests.
    /// </summary>
    protected virtual async Task<string> CallAzureDiAsync(
        string modelId,
        Stream fileStream,
        CancellationToken ct)
    {
        var client = BuildClient();
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed, modelId, fileStream, cancellationToken: ct);

        var result = operation.Value;
        logger.LogInformation(
            "Document Intelligence: {Pages} page(s) analyzed", result.Pages.Count);
        return result.Content;
    }

    private DocumentAnalysisClient BuildClient()
    {
        var endpoint = new Uri(options.Value.Endpoint);
        var apiKey   = options.Value.ApiKey;

        return string.IsNullOrWhiteSpace(apiKey)
            ? new DocumentAnalysisClient(endpoint, new DefaultAzureCredential())
            : new DocumentAnalysisClient(endpoint, new AzureKeyCredential(apiKey));
    }
}
