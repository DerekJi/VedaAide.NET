namespace Veda.Core.Interfaces;

/// <summary>演示文档库条目（来自 Blob Storage demo-documents/ 前缀）。</summary>
public record DemoDocument(
    string Name,
    string Description,
    long   SizeBytes,
    string Extension);

/// <summary>
/// 演示文档库服务契约：列出预置示例文档，并支持一键 ingest 到知识库。
/// </summary>
public interface IDemoLibraryService
{
    /// <summary>列出 Blob Storage demo-documents/ 前缀下的所有可用示例文档。</summary>
    Task<IReadOnlyList<DemoDocument>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// 将指定示例文档从 Blob Storage 下载并 ingest 到知识库（scope=Public）。
    /// </summary>
    Task<IngestResult> IngestAsync(string blobName, CancellationToken ct = default);
}
