# 阶段一 RAG 引擎测试方案

## 1. 测试范围

阶段一覆盖以下核心组件：

| 组件 | 测试类型 | 测试文件 |
|---|---|---|
| `VectorMath` | 单元 | `Veda.Core.Tests/VectorMathTests.cs` |
| `ChunkingOptions` | 单元 | `Veda.Core.Tests/ChunkingOptionsTests.cs` |
| `DocumentTypeParser` | 单元 | `Veda.Core.Tests/DocumentTypeParserTests.cs` |
| `TextDocumentProcessor` | 单元 | `Veda.Services.Tests/TextDocumentProcessorTests.cs` |
| `DocumentIngestService` | 单元（Mock） | `Veda.Services.Tests/DocumentIngestServiceTests.cs` |
| `QueryService` | 单元（Mock） | `Veda.Services.Tests/QueryServiceTests.cs` |
| `SqliteVectorStore` | 集成（临时 SQLite） | `Veda.Services.Tests/SqliteVectorStoreIntegrationTests.cs` |
| `POST /api/documents` | 冒烟 | `scripts/smoke-test.sh` |
| `POST /api/query` | 冒烟 | `scripts/smoke-test.sh` |


## 2. 冒烟测试方案

详见 [`scripts/smoke-test.sh`](../../scripts/smoke-test.sh)。

| 测试点 | 验证内容 |
|---|---|
| Swagger 可访问 | API 存活检查 |
| `POST /api/documents` | 返回 201 + documentId + chunksStored |
| `POST /api/documents`（空内容） | 返回 400 |
| `POST /api/query` | 返回 200 + answer + sources + answerConfidence |
| `POST /api/query`（空问题） | 返回 400 |


## 3. 测试执行命令

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
