namespace Veda.Core.Interfaces;

/// <summary>
/// 文档摄取服务契约（写操作）。
/// ISP：与查询操作分离，Controller 只依赖它需要的接口。
/// </summary>
public interface IDocumentIngestor
{
    /// <summary>
    /// 摄取纯文本文档：分块 → Embedding → 去重 → 存储。
    /// </summary>
    /// <returns>包含 DocumentId 的结果，调用方可用于后续删除操作。</returns>
    Task<IngestResult> IngestAsync(
        string content,
        string documentName,
        DocumentType documentType,
        CancellationToken ct = default);

    /// <summary>
    /// 摄取二进制文件（图片 / PDF）：文件提取 → 分块 → Embedding → 去重 → 存储。
    /// 路由策略：<see cref="DocumentType.RichMedia"/> 使用 Vision 模型；其余使用 Azure AI Document Intelligence。
    /// </summary>
    /// <param name="fileStream">图片或 PDF 文件流。</param>
    /// <param name="fileName">原始文件名（含扩展名），用于日志与元数据。</param>
    /// <param name="mimeType">MIME 类型（如 image/jpeg、application/pdf）。</param>
    /// <param name="documentType">文档类型，决定提取模型与分块策略。</param>
    Task<IngestResult> IngestFileAsync(
        Stream fileStream,
        string fileName,
        string mimeType,
        DocumentType documentType,
        CancellationToken ct = default);
}
