namespace Veda.Core.Interfaces;

/// <summary>
/// 文档摄取服务契约（写操作）。
/// ISP：与查询操作分离，Controller 只依赖它需要的接口。
/// </summary>
public interface IDocumentIngestor
{
    /// <summary>
    /// 摄取文档：分块 → Embedding → 去重 → 存储。
    /// </summary>
    /// <returns>包含 DocumentId 的结果，调用方可用于后续删除操作。</returns>
    Task<IngestResult> IngestAsync(
        string content,
        string documentName,
        DocumentType documentType,
        CancellationToken ct = default);
}
