using Veda.Core.Options;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using Veda.Core;

namespace Veda.Services;

/// <summary>
/// 基于 Azure AI Document Intelligence 的文件内容提取实现。
/// 路由策略：
///   - BillInvoice → "prebuilt-invoice"（结构化发票/账单提取）
///   - 其余类型   → "prebuilt-read"（通用 OCR + 版式感知）
/// 输出 <see cref="Azure.AI.FormRecognizer.DocumentAnalysis.AnalyzeResult.Content"/>，
/// 即文档全文的 Markdown 表示，直接进入现有文本分块管线。
///
/// 配额感知：429 响应时自动标记 <see cref="AzureDiQuotaState"/> 并抛出
/// <see cref="QuotaExceededException"/>，上层服务可捕获并降级到 Vision 模型。
/// 后续同月请求优先读取内存状态，跳过对 Azure DI 的实际调用。
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
        // 配额超限快速路径：直接抛出，不消耗 fileStream
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
    /// 实际调用 Azure SDK 的隔离点。子类可重写以在测试中注入不同行为。
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
