using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Veda.Services;

/// <summary>
/// Multimodal Vision file content extractor.
/// Used for scanned document OCR (fallback when Azure DI is not configured or quota exceeded)
/// and for RichMedia document type.
/// Supports AzureOpenAI (gpt-4o-mini) and multimodal Ollama models (e.g. qwen3-vl:8b).
/// Requires <c>Veda:Vision:Enabled = true</c> in configuration.
/// </summary>
public sealed class VisionModelFileExtractor(
    [FromKeyedServices("vision")] IChatCompletionService visionChat,
    IOptions<VisionOptions> options,
    ILogger<VisionModelFileExtractor> logger) : IFileExtractor
{
    public async Task<string> ExtractAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            throw new InvalidOperationException(
                "Vision extraction is not enabled. Set Veda:Vision:Enabled = true.");

        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms, ct);
        var imageBytes = ms.ToArray();

        logger.LogInformation(
            "Vision extraction for '{Name}' ({MimeType}, {Bytes} bytes)",
            fileName, mimeType, imageBytes.Length);

        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(new ChatMessageContentItemCollection
        {
            new TextContent(BuildExtractionPrompt(documentType)),
            new ImageContent(BinaryData.FromBytes(imageBytes), mimeType)
        });

        var results = await visionChat.GetChatMessageContentsAsync(chatHistory, cancellationToken: ct);
        var text = string.Concat(results.Select(r => r.Content));

        logger.LogInformation(
            "Vision extraction complete for '{Name}': {Chars} chars extracted",
            fileName, text.Length);

        return text;
    }

    private static string BuildExtractionPrompt(DocumentType documentType) => documentType switch
    {
        DocumentType.BillInvoice =>
            "请仔细分析这张账单/发票图片，结构化提取以下字段：\n" +
            "- 商家名称 / 收款方\n" +
            "- 账单日期\n" +
            "- 各明细项：品名、数量、单价、小计\n" +
            "- 折扣/优惠（如有）\n" +
            "- 小计、税额（税率）、总金额\n" +
            "- 支付方式（如有）\n" +
            "以\"字段名：字段值\"格式逐行输出，保留所有数字和单位，不要遗漏任何金额或项目。",
        DocumentType.Identity =>
            "请仔细分析这张证件图片（护照/身份证/驾照），结构化提取以下字段：\n" +
            "- 证件类型\n" +
            "- 姓名（中文/英文）\n" +
            "- 证件号码\n" +
            "- 出生日期\n" +
            "- 性别\n" +
            "- 国籍/发证国家\n" +
            "- 签发机关\n" +
            "- 签发日期\n" +
            "- 有效期\n" +
            "以\"字段名：字段值\"格式逐行输出，如某字段图片中不存在则跳过，不要编造信息。",
        _ =>
            "请详细描述并提取这张图片的所有内容，包括：\n" +
            "1. 所有印刷文字（题目、说明、标注）\n" +
            "2. 所有手写文字（答案、评语、笔记）\n" +
            "3. 图形/图表的含义（几何形状、坐标轴、数据走势等）\n" +
            "4. 符号标注（对勾✓表示正确、叉叉✗表示错误、批改符号等）\n" +
            "输出结构化文本，确保图形语义和批注信息完整，按区域/题目编号组织内容。"
    };
}
