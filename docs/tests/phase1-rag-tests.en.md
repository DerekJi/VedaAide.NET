# Phase 1 RAG Engine Test Plan

> 中文版见 [phase1-rag-tests.cn.md](phase1-rag-tests.cn.md)

## 1. Test Scope

Phase 1 covers the following core components:

| Component | Test Type | Test File |
|---|---|---|
| `VectorMath` | Unit | `Veda.Core.Tests/VectorMathTests.cs` |
| `ChunkingOptions` | Unit | `Veda.Core.Tests/ChunkingOptionsTests.cs` |
| `DocumentTypeParser` | Unit | `Veda.Core.Tests/DocumentTypeParserTests.cs` |
| `TextDocumentProcessor` | Unit | `Veda.Services.Tests/TextDocumentProcessorTests.cs` |
| `DocumentIngestService` | Unit (Mock) | `Veda.Services.Tests/DocumentIngestServiceTests.cs` |
| `QueryService` | Unit (Mock) | `Veda.Services.Tests/QueryServiceTests.cs` |
| `SqliteVectorStore` | Integration (temp SQLite) | `Veda.Services.Tests/SqliteVectorStoreIntegrationTests.cs` |
| `POST /api/documents` | Smoke | `scripts/smoke-test.sh` |
| `POST /api/query` | Smoke | `scripts/smoke-test.sh` |


## 2. Smoke Test Plan

See [`scripts/smoke-test.sh`](../../scripts/smoke-test.sh) for the full script.

| Test Point | What is Verified |
|---|---|
| Swagger accessible | API liveness check |
| `POST /api/documents` | Returns 201 + documentId + chunksStored |
| `POST /api/documents` (empty content) | Returns 400 |
| `POST /api/query` | Returns 200 + answer + sources + answerConfidence |
| `POST /api/query` (empty question) | Returns 400 |


## 3. Test Execution Commands

```bash
# Run Core unit tests only
dotnet test tests/Veda.Core.Tests/

# Run Services unit tests only (includes integration)
dotnet test tests/Veda.Services.Tests/

# Run all tests with coverage report
dotnet test --collect:"XPlat Code Coverage"

# Run smoke tests (ensure API is running on port 5126)
./scripts/smoke-test.sh
```
