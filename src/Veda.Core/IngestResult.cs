namespace Veda.Core;

/// <summary>
/// 文档摄取操作的结果，包含调用方需要的 DocumentId（用于后续删除）。
/// </summary>
public record IngestResult(string DocumentId, string DocumentName, int ChunksStored);
