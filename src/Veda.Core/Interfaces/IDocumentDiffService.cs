namespace Veda.Core.Interfaces;

/// <summary>文档版本对比服务接口，用于生成结构化变更摘要。</summary>
public interface IDocumentDiffService
{
    /// <summary>对比新旧版本文档内容，生成结构化变更摘要。</summary>
    Task<DocumentChangeSummary> DiffAsync(
        string documentId,
        string oldContent,
        string newContent,
        CancellationToken ct = default);
}

/// <summary>文档变更摘要。</summary>
public record DocumentChangeSummary(
    string DocumentId,
    int AddedChunks,
    int RemovedChunks,
    int ModifiedChunks,
    IReadOnlyList<string> ChangedTopics,
    DateTimeOffset ChangedAt);
