# Phase 4/5 Test Plan: Agent + MCP + Prompt + External Data Sources

> 中文版见 [phase4-agent-mcp-tests.cn.md](phase4-agent-mcp-tests.cn.md)

## 1. Test Scope

Phase 4/5 introduces the following components, all requiring unit test coverage:

| Component | Test Type | Test File | Status |
|------|---------|---------|------|
| `OrchestrationService` (deterministic call chain) | Unit (Mock) | `Veda.Services.Tests/OrchestrationServiceTests.cs` | ✅ Done |
| `KnowledgeBaseTools` (MCP search + list) | Unit (Mock) | `Veda.Services.Tests/KnowledgeBaseToolsTests.cs` | ✅ Done |
| `IngestTools` (MCP ingest) | Unit (Mock) | `Veda.Services.Tests/IngestToolsTests.cs` | ✅ Done |
| `ContextWindowBuilder` (token budget selection) | Unit | `Veda.Core.Tests/ContextWindowBuilderTests.cs` | ✅ Done |
| `ChainOfThoughtStrategy` (CoT enhancement) | Unit | `Veda.Core.Tests/ChainOfThoughtStrategyTests.cs` | ✅ Done |
| `FileSystemConnector` (local directory ingestion) | Unit (Mock + temp dir) | `Veda.Services.Tests/FileSystemConnectorTests.cs` | ✅ Done |

> `LlmOrchestrationService` depends on the real SK `ChatCompletionAgent` and is difficult to mock LLM behavior for unit tests. It is currently covered via integration/smoke tests.

---

## 2. OrchestrationService (Deterministic Call Chain)

**Test goal:** Verify calling order and return value format for `RunQueryFlowAsync` and `RunIngestFlowAsync`.

| Test Method | Scenario | Expected |
|---------|------|------|
| `RunQueryFlowAsync_ValidQuestion_ShouldCallQueryService` | Normal Q&A | `IQueryService.QueryAsync` called once |
| `RunQueryFlowAsync_WhenNotHallucination_ShouldCallEvalAgent` | Answer not hallucination + has sources | `IHallucinationGuardService.VerifyAsync` called, `IsEvaluated = true` |
| `RunQueryFlowAsync_WhenHallucination_ShouldSkipEvalAgent` | Answer flagged as hallucination | `VerifyAsync` not called, `AgentTrace` contains "skipped" |
| `RunIngestFlowAsync_ValidContent_ShouldCallDocumentIngestor` | Normal ingestion | `IDocumentIngestor.IngestAsync` called, `Answer` contains chunk count |
| `RunIngestFlowAsync_InvoiceFileName_ShouldInferBillInvoiceType` | Filename contains "invoice" | `IngestAsync` called with `documentType = BillInvoice` |

---

## 3. KnowledgeBaseTools (MCP Tools)

**Test goal:** Verify tools return correct JSON format and handle edge cases properly.

| Test Method | Scenario | Expected |
|---------|------|------|
| `SearchKnowledgeBase_WithResults_ShouldReturnJsonArray` | Vector store has matching results | Returns JSON array with `documentName`, `content`, `similarity`, `documentType` fields |
| `SearchKnowledgeBase_NoResults_ShouldReturnEmptyArray` | No matches in vector store | Returns `[]` |
| `SearchKnowledgeBase_EmptyQuery_ShouldThrowArgumentException` | Empty/blank query | Throws `ArgumentException` |
| `SearchKnowledgeBase_TopKClamped_ShouldClampTo20` | topK = 100 | Actual `SearchAsync` call uses topK = 20 |
| `SearchKnowledgeBase_ContentOver500Chars_ShouldTruncate` | Chunk content > 500 chars | JSON content field ends with "..." |
| `ListDocuments_MultipleChunks_ShouldGroupByDocument` | Same document has multiple chunks | JSON groups by documentId, `chunkCount` is correct |

---

## 4. IngestTools (MCP Tools)

| Test Method | Scenario | Expected |
|---------|------|------|
| `IngestDocument_ValidInput_ShouldReturnJsonWithDocumentId` | Normal ingestion | Returns JSON with `documentId`, `chunksStored`, `documentName` |
| `IngestDocument_EmptyContent_ShouldThrowArgumentException` | Empty/blank content | Throws `ArgumentException` |
| `IngestDocument_EmptyDocumentName_ShouldThrowArgumentException` | Empty/blank documentName | Throws `ArgumentException` |
| `IngestDocument_InvalidDocumentType_ShouldFallbackToOther` | documentType = "Unknown" | Parsed as `DocumentType.Other` |

---

## 5. ContextWindowBuilder (Token Budget)

| Test Method | Scenario | Expected |
|---------|------|------|
| `Build_EmptyCandidates_ShouldReturnEmpty` | Empty candidates | Returns empty list |
| `Build_AllFitInBudget_ShouldReturnAll` | Total chars of all chunks < budget | All returned, sorted by similarity descending |
| `Build_ExceedsBudget_ShouldStopAtLimit` | Exceeds token budget | Only chunks within budget returned (greedy cutoff) |
| `Build_SortsBySimilarity_ShouldSelectHighestFirst` | Candidates have out-of-order similarities | Highest similarity chunks selected first |
| `Build_SingleLargeChunk_ExceedsBudget_ShouldReturnEmpty` | Single chunk already exceeds budget | Returns empty list (none rather than overflow) |

---

## 6. ChainOfThoughtStrategy (CoT Prompt Enhancement)

| Test Method | Scenario | Expected |
|---------|------|------|
| `Enhance_ValidInput_ShouldContainContextAndQuestion` | Normal call | Returned string contains the original context and question text |
| `Enhance_ValidInput_ShouldContainCoTInstruction` | Normal call | Returned string contains step-reasoning keywords ("step" or "reason") |

---

## 7. FileSystemConnector (External Data Source)

| Test Method | Scenario | Expected |
|---------|------|------|
| `SyncAsync_WhenDisabled_ShouldSkipWithZeroCounts` | `Enabled = false` | `IngestAsync` not called, `FilesProcessed = 0` |
| `SyncAsync_WhenPathNotConfigured_ShouldSkipWithZeroCounts` | Path is empty | `IngestAsync` not called, `FilesProcessed = 0` |
| `SyncAsync_WithValidFiles_ShouldCallIngestForEachFile` | Directory has 2 `.txt` files | `IngestAsync` called 2 times, `FilesProcessed = 2` |
| `SyncAsync_WhenIngestThrows_ShouldContinueAndRecordError` | One file throws during ingestion | Continues processing remaining files, `Errors` list contains failure info |
| `SyncAsync_NonMatchingExtension_ShouldSkipFile` | Directory has only `.pdf` files | `IngestAsync` not called |
| `SyncAsync_WhenFileUnchanged_ShouldSkipIngest` | File content hash matches previously recorded hash | `IngestAsync` not called, `FilesProcessed = 0` |
| `SyncAsync_WhenFileContentChanged_ShouldReIngest` | Same path, but content has changed | `IngestAsync` called once — re-ingested |

---

## 8. Notes

- All tests follow the `MethodName_Scenario_ShouldAction` naming convention (see [test-conventions.en.md](test-conventions.en.md)).
- Assertions use FluentAssertions — `Assert.*` is prohibited.
- `FileSystemConnector` tests use `IDocumentIngestor` Mock + `ISyncStateStore` Mock + a temp directory (auto-cleaned in `[TearDown]`). **No dependency on pre-existing filesystem paths** — safe to run in CI environments.
- Content-hash skip logic: the three scenarios (never synced / synced with same hash / synced with different hash) are simulated by controlling the return value of `ISyncStateStore.GetContentHashAsync` in the Mock setup.
