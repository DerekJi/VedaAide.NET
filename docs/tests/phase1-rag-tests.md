# 阶段一 RAG 引擎测试方案

## 1. 测试范围

阶段一覆盖以下核心组件：

| 组件 | 测试类型 | 测试项目 |
|---|---|---|
| `ChunkingOptions` | 单元 | `Veda.Core.Tests` |
| `VectorMath` | 单元 | `Veda.Core.Tests` |
| `DocumentTypeParser` | 单元 | `Veda.Core.Tests` |
| `TextDocumentProcessor` | 单元 | `Veda.Services.Tests` |
| `DocumentIngestService` | 单元（Mock） | `Veda.Services.Tests` |
| `QueryService` | 单元（Mock） | `Veda.Services.Tests` |
| `SqliteVectorStore` | 集成（临时 SQLite） | `Veda.Services.Tests` |
| `POST /api/documents` | 冒烟 | `scripts/smoke-test.sh` |
| `POST /api/query` | 冒烟 | `scripts/smoke-test.sh` |


## 2. 单元测试方案

### 2.1 `VectorMath.CosineSimilarity`

```csharp
[Fact]
public void CosineSimilarity_IdenticalVectors_ReturnsOne()
// [Fact]  CosineSimilarity_OppositeVectors_ReturnsNegativeOrZero
// [Fact]  CosineSimilarity_DifferentLengths_ReturnsZero
// [Fact]  CosineSimilarity_ZeroVector_ReturnsZero
```

### 2.2 `ChunkingOptions.ForDocumentType`

```csharp
[Theory]
[InlineData(DocumentType.BillInvoice,  256,  32)]
[InlineData(DocumentType.Specification, 1024, 128)]
[InlineData(DocumentType.Report,        512,  64)]
[InlineData(DocumentType.Other,         512,  64)]
public void ForDocumentType_ReturnsExpectedTokenSize(DocumentType type, int expectedToken, int expectedOverlap)
```

### 2.3 `TextDocumentProcessor.Process`

```csharp
// [Fact]  Process_EmptyContent_ThrowsArgumentException
// [Fact]  Process_EmptyDocumentId_ThrowsArgumentException
// [Fact]  Process_ShortContent_ReturnsSingleChunk
// [Fact]  Process_LongContent_SplitsIntoMultipleChunksWithOverlap
// [Theory] Process_AllDocumentTypes_UsesCorrectChunkSize
```

### 2.4 `DocumentIngestService.IngestAsync`

使用 Mock 替换 `IDocumentProcessor`、`IEmbeddingService`、`IVectorStore`：

```csharp
// [Fact]  IngestAsync_ValidInput_CallsProcessorAndStoresChunks
// [Fact]  IngestAsync_ReturnsDocumentIdInResult
// [Fact]  IngestAsync_EmptyContent_ThrowsArgumentException
// [Fact]  IngestAsync_EmbeddingCountMatchesChunkCount
```

### 2.5 `QueryService.QueryAsync`

```csharp
// [Fact]  QueryAsync_NoResultsFromVectorStore_ReturnsNoInfoMessage
// [Fact]  QueryAsync_ResultsFound_ReturnsAnswerWithSources
// [Fact]  QueryAsync_AnswerConfidenceEqualsMaxSimilarity
// [Fact]  QueryAsync_EmptyQuestion_ThrowsArgumentException
```

### 2.6 `DocumentTypeParser`

```csharp
// [Theory] ParseOrDefault_ValidString_ReturnsCorrectType
// [Theory] ParseOrDefault_InvalidString_ReturnsDefaultType
// [Theory] ParseOrNull_ValidString_ReturnsNullableType
// [Fact]   ParseOrNull_NullOrEmpty_ReturnsNull
```


## 3. 集成测试方案

使用 `SqliteVectorStore` + 真实 SQLite 内存数据库（`Data Source=:memory:`）验证：

```csharp
// [Fact]  UpsertBatchAsync_StoresChunks_CanBeSearched
// [Fact]  UpsertBatchAsync_DuplicateContent_SkipsDuplicates
// [Fact]  SearchAsync_ReturnsResultsAboveMinSimilarity
// [Fact]  SearchAsync_FilterByDocumentType_ReturnsOnlyMatchingType
// [Fact]  DeleteByDocumentAsync_RemovesAllChunksForDocument
```

> **注意：** 集成测试中的 Embedding 需使用固定维度的随机向量（Mock），避免真实 Ollama 依赖。


## 4. 冒烟测试方案

详见 [`scripts/smoke-test.sh`](../../scripts/smoke-test.sh)。

| 测试点 | 验证内容 |
|---|---|
| Swagger 可访问 | API 存活检查 |
| `POST /api/documents` | 返回 200 + documentId + chunksStored |
| `POST /api/documents`（空内容） | 返回 400 |
| `POST /api/query` | 返回 200 + answer + sources + answerConfidence |
| `POST /api/query`（空问题） | 返回 400 |


## 5. 测试执行命令

```bash
# 仅运行 Core 单元测试
dotnet test tests/Veda.Core.Tests/

# 仅运行 Services 单元测试（含集成）
dotnet test tests/Veda.Services.Tests/

# 运行所有测试并生成覆盖率报告
dotnet test --collect:"XPlat Code Coverage"

# 运行冒烟测试（确保 API 已启动在 5126 端口）
./scripts/smoke-test.sh
```
