# Integration Testing 指南

## 概述

本文档说明 VedaAide 集成测试的设计思路、执行方式，以及如何在不依赖任何外部服务的情况下验证完整的 Ingest → 向量检索 → LLM 问答 pipeline。

---

## 测试分层

| 层级 | 存储 | AI 服务 | 代表文件 |
|------|------|---------|---------|
| 单元测试 | `Mock<IVectorStore>` | 全部 Mock | `QueryServiceTests.cs` |
| 存储集成测试 | SQLite in-memory | 无 | `SqliteVectorStoreIntegrationTests.cs` |
| **Pipeline 集成测试** | **SQLite in-memory** | **Fake Stub** | **`Integration/IngestQueryIntegrationTests.cs`** |
| 手动烟测 | 磁盘 `veda.db` | Ollama / Azure | `test-data/questions.md` |

---

## Pipeline 集成测试设计

### 核心思路

用 **SQLite `DataSource=:memory:`** 替代磁盘 `veda.db`，用 Fake 服务替代 Ollama / Azure OpenAI：

- 不写入任何磁盘文件
- 每个测试用例有独立隔离的 DB 实例（`[TearDown]` 自动销毁）
- 无需启动 Ollama 或配置 Azure 密钥
- 可在 CI/CD 环境中零配置运行

### Fake 服务说明

**`FakeEmbeddingService`**

基于 SHA-256 哈希生成确定性单位向量（384 维）：

- 相同文本 → 相同向量（cosine = 1.0）— 使查询能精确命中已 ingest 的文档
- 不同文本 → 不同向量 — 向量检索区分度正常工作
- 无需任何外部进程或网络调用

**`FakeChatService`**

将 `userMessage`（含检索到的上下文）原样作为答案返回，使断言可以直接验证哪些 chunk 被检索到了，而不需要关心 LLM 的生成质量。

### 测试架构

```
SqliteConnection("DataSource=:memory:")
    └── VedaDbContext (EF Core)
         └── SqliteVectorStore ← 真实实现
              ├── DocumentIngestService ← 真实实现
              │    ├── TextDocumentProcessor  (真实：分块)
              │    ├── FakeEmbeddingService   (替换 Ollama)
              │    └── file extractors        (Stub：测试不调用)
              └── QueryService ← 真实实现
                   ├── FakeEmbeddingService   (替换 Ollama)
                   └── FakeChatService        (替换 LLM)
```

---

## 如何运行

### 仅运行集成测试

```bash
dotnet test tests/Veda.Services.Tests --filter "Category=Integration"
```

### 运行所有测试（含集成）

```bash
dotnet test tests/Veda.Services.Tests
```

### 跳过集成测试（仅单元测试）

```bash
dotnet test tests/Veda.Services.Tests --filter "Category!=Integration"
```

---

## 本地开发：API 级别手动烟测

当需要用真实 Ollama + 真实 PDF 文件进行端到端验证时，使用以下流程。

### DevBypass 认证机制

所有 API 端点默认要求 Entra ID JWT。在 Development 模式下，可通过 `DevBypassAuthHandler` 绕过认证，所有请求以固定身份 `oid=dev-user` 通过。

**激活条件（两个条件同时成立）：**

1. `ASPNETCORE_ENVIRONMENT=Development`
2. `appsettings.Development.json` 中设置 `"Veda": { "DevMode": { "NoAuth": true } }`

> **警告：** 生产环境严禁开启 `NoAuth=true`。

### 1. 启动 API

```bash
cd src/Veda.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

### 2. Ingest PDF

```bash
curl -X POST 'http://localhost:5126/api/documents/upload?documentType=Certificate&documentName=ICAS-Maths' \
  -F 'file=@test-data/ICAS-Year5.Maths.pdf;type=application/pdf'
```

### 3. 查询

```bash
curl -X POST http://localhost:5126/api/query \
  -H "Content-Type: application/json" \
  -d '{"question": "How is Marco'\''s maths result?", "topK": 5, "minSimilarity": 0.3}'
```

### 4. 清理测试数据（可选）

```bash
curl -X DELETE -H "X-Confirm: yes" http://localhost:5126/api/admin/data
```

---

## Certificate 类型的 DedupThreshold 说明

`ChunkingOptions.Certificate` 的 `DedupThreshold = 1.0f`，实际上禁用了语义去重。

**原因：** ICAS 同类证书（英语/数学/科学）采用相同模板，嵌入向量的余弦相似度 > 0.97。原始设计值 0.70 导致不同科目证书互相误判为近重复而丢失。

`1.0f` 表示只有余弦相似度完全达到 1.0 时才判定为近重复（实际上不可能），去重仍通过 `ContentHash`（SHA-256）保证完全相同内容不重复存储。
