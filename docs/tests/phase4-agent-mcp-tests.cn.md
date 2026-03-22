# 阶段四/五 测试方案：Agent + MCP + Prompt + 外部数据源

## 1. 测试范围

阶段四/五新增以下组件，均需单元测试覆盖：

| 组件 | 测试类型 | 测试文件 | 状态 |
|------|---------|---------|------|
| `OrchestrationService`（确定性调用链） | 单元（Mock） | `Veda.Services.Tests/OrchestrationServiceTests.cs` | ✅ 已完成 |
| `KnowledgeBaseTools`（MCP search + list） | 单元（Mock） | `Veda.Services.Tests/KnowledgeBaseToolsTests.cs` | ✅ 已完成 |
| `IngestTools`（MCP ingest） | 单元（Mock） | `Veda.Services.Tests/IngestToolsTests.cs` | ✅ 已完成 |
| `ContextWindowBuilder`（Token 预算选取） | 单元 | `Veda.Core.Tests/ContextWindowBuilderTests.cs` | ✅ 已完成 |
| `ChainOfThoughtStrategy`（CoT 增强） | 单元 | `Veda.Core.Tests/ChainOfThoughtStrategyTests.cs` | ✅ 已完成 |
| `FileSystemConnector`（本地目录摄取） | 单元（Mock + 临时目录） | `Veda.Services.Tests/FileSystemConnectorTests.cs` | ✅ 已完成 |

> `LlmOrchestrationService` 依赖真实 SK `ChatCompletionAgent`，难以在单元测试中 Mock LLM 行为，当前通过集成/冒烟测试验证。

---

## 2. OrchestrationService（确定性调用链）

**测试目标：** 验证 RunQueryFlowAsync 和 RunIngestFlowAsync 的调用顺序和返回值格式。

| 测试方法 | 场景 | 预期 |
|---------|------|------|
| `RunQueryFlowAsync_ValidQuestion_ShouldCallQueryService` | 正常问答 | `IQueryService.QueryAsync` 被调用一次 |
| `RunQueryFlowAsync_WhenNotHallucination_ShouldCallEvalAgent` | 回答非幻觉 + 有 sources | `IHallucinationGuardService.VerifyAsync` 被调用，`IsEvaluated = true` |
| `RunQueryFlowAsync_WhenHallucination_ShouldSkipEvalAgent` | 回答为幻觉 | `VerifyAsync` 不被调用，`AgentTrace` 包含 "skipped" |
| `RunIngestFlowAsync_ValidContent_ShouldCallDocumentIngestor` | 正常摄取 | `IDocumentIngestor.IngestAsync` 被调用，`Answer` 包含 chunk 数量 |
| `RunIngestFlowAsync_InvoiceFileName_ShouldInferBillInvoiceType` | 文件名含 "invoice" | `IngestAsync` 被调用时 `documentType = BillInvoice` |

---

## 3. KnowledgeBaseTools（MCP 工具）

**测试目标：** 验证工具返回正确的 JSON 格式，边界条件处理正常。

| 测试方法 | 场景 | 预期 |
|---------|------|------|
| `SearchKnowledgeBase_WithResults_ShouldReturnJsonArray` | 向量库有匹配结果 | 返回 JSON 数组，含 `documentName`、`content`、`similarity`、`documentType` 字段 |
| `SearchKnowledgeBase_NoResults_ShouldReturnEmptyArray` | 向量库无匹配 | 返回 `[]` |
| `SearchKnowledgeBase_EmptyQuery_ShouldThrowArgumentException` | query 为空白 | 抛出 `ArgumentException` |
| `SearchKnowledgeBase_TopKClamped_ShouldClampTo20` | topK = 100 | 实际调用 `SearchAsync` 的 topK = 20 |
| `SearchKnowledgeBase_ContentOver500Chars_ShouldTruncate` | chunk 内容超过 500 字符 | 返回 JSON 中 content 以 "..." 结尾 |
| `ListDocuments_MultipleChunks_ShouldGroupByDocument` | 同一文档有多个 chunk | 返回 JSON 按 documentId 分组，`chunkCount` 正确 |

---

## 4. IngestTools（MCP 工具）

| 测试方法 | 场景 | 预期 |
|---------|------|------|
| `IngestDocument_ValidInput_ShouldReturnJsonWithDocumentId` | 正常摄取 | 返回 JSON 含 `documentId`、`chunksStored`、`documentName` |
| `IngestDocument_EmptyContent_ShouldThrowArgumentException` | content 为空白 | 抛出 `ArgumentException` |
| `IngestDocument_EmptyDocumentName_ShouldThrowArgumentException` | documentName 为空白 | 抛出 `ArgumentException` |
| `IngestDocument_InvalidDocumentType_ShouldFallbackToOther` | documentType = "Unknown" | 解析为 `DocumentType.Other` |

---

## 5. ContextWindowBuilder（Token 预算）

| 测试方法 | 场景 | 预期 |
|---------|------|------|
| `Build_EmptyCandidates_ShouldReturnEmpty` | 候选为空 | 返回空列表 |
| `Build_AllFitInBudget_ShouldReturnAll` | 所有块总字符 < 预算 | 全部返回，按相似度降序排列 |
| `Build_ExceedsBudget_ShouldStopAtLimit` | 超出 Token 预算 | 只返回在预算内的块（贪心截断） |
| `Build_SortsBySimilarity_ShouldSelectHighestFirst` | 候选相似度乱序 | 优先选取相似度最高的块 |
| `Build_SingleLargeChunk_ExceedsBudget_ShouldReturnEmpty` | 单块即超过预算 | 返回空列表（宁缺毋过） |

---

## 6. ChainOfThoughtStrategy（CoT 提示增强）

| 测试方法 | 场景 | 预期 |
|---------|------|------|
| `Enhance_ValidInput_ShouldContainContextAndQuestion` | 正常调用 | 返回字符串包含 context 和 question 原文 |
| `Enhance_ValidInput_ShouldContainCoTInstruction` | 正常调用 | 返回字符串包含步骤引导关键词（"步骤" 或 "推导"） |

---

## 7. FileSystemConnector（外部数据源）

| 测试方法 | 场景 | 预期 |
|---------|------|------|
| `SyncAsync_WhenDisabled_ShouldSkipWithZeroCounts` | `Enabled = false` | 不调用 `IngestAsync`，`FilesProcessed = 0` |
| `SyncAsync_WhenPathNotConfigured_ShouldSkipWithZeroCounts` | Path 为空 | 不调用 `IngestAsync`，`FilesProcessed = 0` |
| `SyncAsync_WithValidFiles_ShouldCallIngestForEachFile` | 目录有 2 个 .txt 文件 | `IngestAsync` 被调用 2 次，`FilesProcessed = 2` |
| `SyncAsync_WhenIngestThrows_ShouldContinueAndRecordError` | 某个文件摄取抛异常 | 继续处理其余文件，`Errors` 列表含失败信息 |
| `SyncAsync_NonMatchingExtension_ShouldSkipFile` | 目录只有 .pdf 文件 | `IngestAsync` 不被调用 |
| `SyncAsync_WhenFileUnchanged_ShouldSkipIngest` | 文件内容哈希与已记录一致 | `IngestAsync` 不被调用，`FilesProcessed = 0` |
| `SyncAsync_WhenFileContentChanged_ShouldReIngest` | 文件路径相同但内容已变更 | `IngestAsync` 被调用一次，重新摄取 |

---

## 8. 备注

- 所有测试遵循 `方法名_场景_ShouldAction` 命名规范（见 [test-conventions.cn.md](test-conventions.cn.md)）。
- 断言使用 FluentAssertions，禁止 `Assert.*`。
- `FileSystemConnector` 测试使用 `IDocumentIngestor` Mock + `ISyncStateStore` Mock + 临时目录（`[TearDown]` 自动清理），**不依赖已存在的文件系统路径**，CI 环境可安全运行。
- 内容哈希跳过逻辑：通过 `ISyncStateStore.GetContentHashAsync` 的 Mock 返回值模拟"未曾同步 / 已同步且内容相同 / 已同步但内容已变"三种场景。
