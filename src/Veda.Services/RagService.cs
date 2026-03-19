// RagService 责任已按 SRP 拆分：
// - 文档摄取 → DocumentIngestService.cs  (实现 IDocumentIngestor)
// - 问答查询 → QueryService.cs           (实现 IQueryService)
// 本文件仅保留以记录重构历史。

namespace Veda.Services;
