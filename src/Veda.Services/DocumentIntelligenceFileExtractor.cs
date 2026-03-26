using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;

namespace Veda.Services;

/// <summary>
/// 基于 Azure AI Document Intelligence 的文件内容提取实现。
/// 路由策略：
///   - BillInvoice → "prebuilt-invoice"（结构化发票/账单提取）
///   - 其余类型   → "prebuilt-read"（通用 OCR + 版式感知）
/// 输出 <see cref="Azure.AI.FormRecognizer.DocumentAnalysis.AnalyzeResult.Content"/>，
/// 即文档全文的 Markdown 表示，直接进入现有文本分块管线。
/// </summary>
public sealed class DocumentIntelligenceFileExtractor(
    IOptions<DocumentIntelligenceOptions> options,
    ILogger<DocumentIntelligenceFileExtractor> logger) : IFileExtractor
{
    public async Task<string> ExtractAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        CancellationToken ct = default)
    {
        if (!options.Value.IsConfigured)
            throw new InvalidOperationException(
                "Azure AI Document Intelligence is not configured. " +
                "Set Veda:DocumentIntelligence:Endpoint in appsettings or environment variables.");

        var modelId = documentType == DocumentType.BillInvoice
            ? "prebuilt-invoice"
            : "prebuilt-read";

        logger.LogInformation(
            "Extracting '{Name}' ({MimeType}) with Document Intelligence model '{Model}'",
            fileName, mimeType, modelId);

        var client = BuildClient();
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed, modelId, fileStream, cancellationToken: ct);

        var result = operation.Value;

        logger.LogInformation(
            "Extracted {Pages} page(s), {Chars} chars from '{Name}'",
            result.Pages.Count, result.Content.Length, fileName);

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
