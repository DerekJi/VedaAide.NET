using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Veda.Services;

/// <summary>
/// 基于 GPT-4o-mini Vision 的文件内容提取实现。
/// 专用于 <see cref="DocumentType.RichMedia"/>：含几何图形、手写批注、符号标注等复杂图文内容。
/// 仅在 <c>Veda:Vision:Enabled = true</c> 且 LlmProvider 为 AzureOpenAI 时有效。
/// </summary>
public sealed class VisionModelFileExtractor(
    IChatCompletionService chatCompletion,
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
                "Vision extraction is not enabled. Set Veda:Vision:Enabled = true " +
                "(requires Veda:LlmProvider = AzureOpenAI with a vision-capable model such as gpt-4o-mini).");

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

        var results = await chatCompletion.GetChatMessageContentsAsync(chatHistory, cancellationToken: ct);
        var text = string.Concat(results.Select(r => r.Content));

        logger.LogInformation(
            "Vision extraction complete for '{Name}': {Chars} chars extracted",
            fileName, text.Length);

        return text;
    }

    private static string BuildExtractionPrompt(DocumentType documentType) => documentType switch
    {
        DocumentType.BillInvoice =>
            "请仔细分析这张账单/发票图片，提取所有文字内容，包括：" +
            "金额、日期、账期、计量单位、明细项目、备注等。" +
            "以结构化文本输出，保留所有数字和关键信息。",
        _ =>
            "请详细描述并提取这张图片的所有内容，包括：\n" +
            "1. 所有印刷文字（题目、说明、标注）\n" +
            "2. 所有手写文字（答案、评语、笔记）\n" +
            "3. 图形/图表的含义（几何形状、坐标轴、数据走势等）\n" +
            "4. 符号标注（对勾✓表示正确、叉叉✗表示错误、批改符号等）\n" +
            "输出结构化文本，确保图形语义和批注信息完整，按区域/题目编号组织内容。"
    };
}
