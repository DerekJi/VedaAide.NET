namespace Veda.Core.Interfaces;

/// <summary>
/// 文件内容提取契约：从二进制文件流（图片 / PDF）中提取可供 RAG 管线消费的纯文本。
/// 路由策略由 <see cref="DocumentIngestService"/> 决定：
///   - <see cref="DocumentType.RichMedia"/> → Vision 模型（GPT-4o-mini）
///   - 其余类型 → Azure AI Document Intelligence
/// </summary>
public interface IFileExtractor
{
    /// <summary>
    /// 从文件流中提取文本内容。
    /// </summary>
    /// <param name="fileStream">图片或 PDF 文件流（只读）。</param>
    /// <param name="fileName">原始文件名，用于日志与错误诊断。</param>
    /// <param name="mimeType">MIME 类型（如 image/jpeg、application/pdf）。</param>
    /// <param name="documentType">文档类型，用于选择提取策略（如 prebuilt-invoice）。</param>
    Task<string> ExtractAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        CancellationToken ct = default);
}
