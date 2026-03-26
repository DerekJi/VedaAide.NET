# VedaAide 二期开发计划与实施路线

> 基准文档：[一期能力差距分析](./stage2-gap-analysis.cn.md)、[系统设计](./system-design.cn.md)
>
> 形成背景：云端部署规划、语义缓存、LLM 路由、安全加固、开发工具及 MCP 外部客户端讨论（2026-03-25）

**文档目标**：定义二期开发范围、模块设计、实施路线及验收标准，作为工程落地的参考基线。

---

## 1. 背景与目标

### 1.1 一期现状

VedaAide 一期（Phase 0–6）已具备完整的本地 RAG 底座：

- 多数据源摄取（FileSystem / Azure Blob）
- 向量检索 + Reranking + 防幻觉
- Agent 编排（Semantic Kernel IRCoT）
- MCP Server（本地 VSCode 集成）
- 评估体系（Faithfulness / Relevancy / ContextRecall）
- RESTful API + GraphQL + SSE 流式问答

### 1.2 二期目标

| 目标 | 说明 |
|------|------|
| **云端部署** | 支持部署到 Azure（Container Apps + CosmosDB + Azure OpenAI + DeepSeek + Blob） |
| **双模式 LLM 路由** | API 暴露 `mode: simple/advanced`，简单查询用 GPT-4o-mini，高级查询用 DeepSeek |
| **语义缓存** | 命中语义相似 Query 时直接返回缓存，降低延迟与 API 成本 |
| **Embedding 提供商切换** | 配置驱动在 Ollama 与 Azure OpenAI 之间切换，不修改业务代码 |
| **安全性加固** | API 鉴权、Managed Identity、Rate Limiting、CORS |
| **开发工具** | DB Admin 端点（查看 / 清空数据），支持开发阶段快速验证 |
| **MCP 外部客户端支持** | `/mcp` 端点对外公开，支持简历网站等第三方客户端接入 |

---

## 2. 云端架构设计

### 2.1 本地 vs 云端对比

| 层 | 本地 | 云端 |
|----|------|------|
| 原始知识库 | 本地文件系统 | Azure Blob Storage（已有 BlobStorageConnector） |
| Embedding | Ollama bge-m3（1024 维） | Azure OpenAI text-embedding-3-small（1536 维） |
| 简单查询 LLM | Ollama qwen3:8b | Azure OpenAI GPT-4o-mini |
| 高级查询 LLM | Ollama qwen3:8b | DeepSeek（via OpenAI 兼容 API） |
| 向量数据库 | SQLite（内存余弦搜索） | Azure CosmosDB Serverless（DiskANN 向量索引） |
| 语义缓存 | 无 | CosmosDB（独立容器，向量相似度命中） |
| Hosting | 本地 `dotnet run` | Azure Container Apps（Consumption，scale-to-zero） |

### 2.2 为何选 Azure Container Apps 而非 Azure Functions

Azure Functions Consumption 计划有根本性限制：

| 限制 | 对本项目的影响 |
|------|----------------|
| 230 秒 HTTP 超时 | SSE 流式端点（`/api/querystream`）无法正常工作 |
| 不支持 SSE 长连接 | MCP Server（`/mcp`）HTTP+SSE 传输依赖长连接 |
| 无后台常驻服务 | `DataSourceSyncBackgroundService` 无法运行 |

Azure Functions Premium（EP1）虽可回避以上限制，但强制保留至少 1 个常驻实例，最低 **~$170/月**，失去 Serverless 成本优势。

**Azure Container Apps（Consumption Workload）** 是更合适的选择：

| 特性 | 说明 |
|------|------|
| Scale-to-zero | 无流量时真正扣 $0（冷启动约 15-30 秒，Demo 可接受） |
| 长连接支持 | 原生支持 SSE、HTTP/2 |
| 后台服务 | `IHostedService` 正常运行 |
| 迁移成本极低 | 项目已有 `Dockerfile`，无需额外改造 |

**预估费用**（Portfolio Demo，约 2 小时/天活跃使用）：

| 资源 | 用量估算 | 月费用 |
|------|----------|--------|
| Container Apps（0.5 vCPU） | ~7200 秒/天活跃 | ~$2.6 |
| CosmosDB Serverless | 轻量读写 + 存储 | ~$1-3 |
| Azure OpenAI（demo 量） | token 按量 | ~$0-2 |
| Blob Storage | < 1 GB | <$0.1 |
| **合计** | | **< $10** |

### 2.3 CosmosDB 数据模型设计

> 使用 CosmosDB for NoSQL 向量搜索（DiskANN），**Serverless 账户支持向量索引**。
> 本地开发使用 CosmosDB Emulator（v2.14.9+ 支持向量搜索）。

#### 容器 1：`VectorChunks`（替换 SQLite）

```json
{
  "id": "chunk-guid",
  "documentId": "doc-guid",
  "documentName": "Q4-Report.md",
  "documentType": "Report",
  "content": "...",
  "chunkIndex": 0,
  "contentHash": "sha256-hex",
  "embeddingModel": "text-embedding-3-small",
  "embedding": [0.1, -0.3, "..."],
  "metadata": {},
  "createdAtTicks": 123456789
}
```

- **Partition Key**：`/documentId` — 高基数，写入均匀分布
- **向量索引**：DiskANN，余弦距离，维度按模型配置（1536 for text-embedding-3-small / 1024 for bge-m3）
- **查询模式**：`VectorDistance()` 函数 + `ORDER BY` 实现 ANN 检索

#### 容器 2：`SemanticCache`（新增）

```json
{
  "id": "cache-guid",
  "questionHash": "sha256-hex",
  "question": "原始问题文本",
  "questionEmbedding": ["..."],
  "answer": "...",
  "sources": ["..."],
  "embeddingModel": "text-embedding-3-small",
  "cacheConfidence": 0.92,
  "createdAt": "2026-03-25T00:00:00Z",
  "_ttl": 86400
}
```

- **Partition Key**：`/questionHash` — 精确匹配快速路径
- **向量索引**：同 VectorChunks，用于语义相似度命中
- **TTL**：通过 CosmosDB `_ttl` 字段自动过期，无需手动清理

---

## 3. 功能模块详细设计

### 3.1 Embedding 提供商切换

**现状**：`ServiceCollectionExtensions.AddVedaAiServices` 硬编码 Ollama。

**重构方案（最小改动）**：`IEmbeddingService` 和 `IChatService` 接口**不变**，只改 DI 注册：

```csharp
// 新签名：接收 IConfiguration，根据配置条件注册
public static IServiceCollection AddVedaAiServices(
    this IServiceCollection services, IConfiguration cfg)
{
    var embeddingProvider = cfg["Veda:EmbeddingProvider"] ?? "Ollama";
    var llmProvider = cfg["Veda:LlmProvider"] ?? "Ollama";
    var kernelBuilder = services.AddKernel();

    if (embeddingProvider == "AzureOpenAI")
        kernelBuilder.AddAzureOpenAIEmbeddingGenerator(
            cfg["Veda:AzureOpenAI:EmbeddingDeployment"]!,
            cfg["Veda:AzureOpenAI:Endpoint"]!,
            cfg["Veda:AzureOpenAI:ApiKey"]!);
    else
        kernelBuilder.AddOllamaEmbeddingGenerator(
            cfg["Veda:EmbeddingModel"] ?? "bge-m3",
            new Uri(cfg["Veda:OllamaEndpoint"] ?? "http://localhost:11434"));

    // Chat 同理（注册为 simple LLM；advanced LLM 单独注册 DeepSeekChatService）
    // ...
}
```

**Embedding 切换迁移流程**（开发阶段）：
1. 修改 `appsettings.json` 中 `EmbeddingProvider`
2. 调用 `DELETE /api/admin/data` 清空向量数据
3. 调用 `POST /api/datasources/sync` 重新摄取

整个流程 < 30 分钟（取决于知识库大小）。

**新增配置项**：

```json
{
  "Veda": {
    "LlmProvider": "Ollama",
    "EmbeddingProvider": "Ollama",
    "AzureOpenAI": {
      "Endpoint": "",
      "ApiKey": "",
      "EmbeddingDeployment": "text-embedding-3-small",
      "ChatDeployment": "gpt-4o-mini"
    },
    "DeepSeek": {
      "BaseUrl": "https://api.deepseek.com/v1",
      "ApiKey": "",
      "ChatModel": "deepseek-chat"
    }
  }
}
```

### 3.2 双模式 LLM 路由

**使用场景**：由调用方根据业务复杂度选择，API 参数显式传入。

#### API 变更

```http
POST /api/query
{
  "question": "...",
  "mode": "simple"   // "simple" | "advanced"，默认 "simple"
}

GET /api/querystream?question=...&mode=advanced
```

#### 实现方案

引入 `ILlmRouter` 接口，根据 `QueryMode` 返回对应 `IChatService` 实现：

```csharp
public enum QueryMode { Simple, Advanced }

public interface ILlmRouter
{
    IChatService Resolve(QueryMode mode);
}
```

- `QueryMode.Simple` → `AzureOpenAIChatService`（GPT-4o-mini）
- `QueryMode.Advanced` → `DeepSeekChatService`（DeepSeek via OpenAI 兼容接口）

`QueryService.QueryAsync` 接收 `QueryMode`，通过 `ILlmRouter` 获取对应服务。

`DeepSeekChatService` 复用 OpenAI SDK，仅配置 `BaseUrl` 和 `ApiKey`，无需新依赖。

### 3.3 语义缓存

**查询流程**：

```
Query 到达
    ↓
生成 Question Embedding
    ↓
[精确路径] QuestionHash 命中 SemanticCache → 直接返回（< 50ms）
    ↓
[语义路径] CosmosDB VectorDistance 搜索 SemanticCache
    similarity >= 阈值（Veda:Rag:SemanticCacheThreshold，默认 0.95）→ 返回缓存
    ↓
[未命中] 走完整 RAG 管道 → 结果写入 SemanticCache（异步，不阻塞响应）
```

**新增接口**（`Veda.Core.Interfaces`）：

```csharp
public interface ISemanticCache
{
    Task<RagQueryResponse?> TryGetAsync(
        float[] queryEmbedding, string questionHash, CancellationToken ct = default);

    Task SetAsync(
        string question, float[] questionEmbedding, string questionHash,
        RagQueryResponse response, CancellationToken ct = default);

    // 文档更新时清除可能失效的缓存
    Task InvalidateByDocumentAsync(string documentId, CancellationToken ct = default);
}
```

**集成点**：
- `QueryService.QueryAsync` 入口处查找缓存；返回前异步写缓存
- `DocumentIngestService.IngestAsync` 完成后调用 `InvalidateByDocumentAsync`
- `AdminController` 暴露 `DELETE /api/admin/cache` 手动清除

**新增配置项**（`Veda:Rag`）：

```json
{
  "Veda": {
    "Rag": {
      "SemanticCacheEnabled": true,
      "SemanticCacheThreshold": 0.95,
      "SemanticCacheTtlSeconds": 86400
    }
  }
}
```

### 3.4 安全性设计

#### 认证方案（API Key，适合 Portfolio Demo）

所有 `/api/*` 端点通过自定义中间件校验 `X-Api-Key` 请求头：

```
X-Api-Key: {api-key}       # 普通操作
X-Api-Key: {admin-key}     # /api/admin/* 端点专用
```

- 密钥在本地通过 User Secrets 配置，云端通过 Container Apps 环境变量注入（不进代码库）
- 后续可无缝升级为 Azure API Management + JWT

#### Managed Identity（云端零密钥）

| Azure 资源 | 所需 RBAC 角色 |
|-----------|--------------|
| Azure Blob Storage | `Storage Blob Data Reader` |
| Azure CosmosDB | `Cosmos DB Built-in Data Contributor` |
| Azure OpenAI | `Cognitive Services OpenAI User` |

代码中使用 `DefaultAzureCredential` 即可（`BlobStorageConnector` 已实现此模式，其他组件同步跟进）。

#### 其他安全项

| 项目 | 实现方式 |
|------|----------|
| HTTPS 强制 | Container Apps Ingress 自动 TLS，无需额外配置 |
| CORS | `AllowedOrigins` 配置化，仅允许简历网站域名等白名单 |
| Rate Limiting | ASP.NET Core `AddRateLimiter`（固定窗口，按 API Key 计 ） |
| 输入长度限制 | `MaxRequestBodySize` + 参数校验（现有 `ArgumentException.ThrowIfNullOrWhiteSpace` 基础上加 length check） |
| MCP 端点认证 | 同 API Key 中间件，MCP 客户端在连接时携带 Header |

### 3.5 开发工具（DB Admin）

新增 `AdminController`，需携带 Admin API Key：

| 端点 | 方法 | 说明 |
|------|------|------|
| `/api/admin/stats` | GET | DB 统计（chunk 总数、文档数、缓存数量、命中率） |
| `/api/admin/chunks` | GET | 分页查看所有 chunks（`?page=1&size=20`） |
| `/api/admin/data` | DELETE | 清空所有向量数据（需附加 `X-Confirm: yes` Header 防误操作） |
| `/api/admin/cache` | DELETE | 清空语义缓存 |
| `/api/admin/documents/{documentId}` | DELETE | 删除指定文档的所有 chunks |

本地开发通过 Swagger UI 或 `Veda.Api.http` 文件调用（已有 `.http` 文件可扩展）。

### 3.6 MCP 外部客户端支持

现有 `/mcp` 端点已完整实现，云端部署后对外公开需要：

1. **认证**：API Key 中间件覆盖 `/mcp` 路径
2. **CORS**：允许简历网站域名
3. **Rate Limiting**：防止滥用（独立限流桶）
4. **公开 URL**：Container Apps 的 Ingress FQDN，例如 `https://vedaaide.{region}.azurecontainerapps.io/mcp`

**对外工具集（现有工具，无需修改）**：

| Tool | 说明 |
|------|------|
| `search_knowledge_base` | 向量检索知识库，返回相关文档片段 |
| `ingest_document` | 写入新文档（如需限制，可通过 IP 白名单或移除此 Tool 的外部暴露） |
| `list_documents` | 列出知识库中所有文档 |

**简历网站集成示例**：在前端 AI 聊天组件中，通过 HTTP+SSE 连接 MCP Server，调用 `search_knowledge_base` 实现"询问项目经验"类 RAG 功能：

```javascript
// 伪代码示例
const mcp = new McpClient("https://vedaaide.xxx.azurecontainerapps.io/mcp", {
  headers: { "X-Api-Key": PUBLIC_API_KEY }
});
const results = await mcp.callTool("search_knowledge_base", { query: userQuestion });
```

---

## 4. 新增 / 修改模块汇总

### 新增

| 模块 / 文件 | 说明 |
|-------------|------|
| `Veda.Storage/CosmosDbVectorStore.cs` | 实现 `IVectorStore`，DiskANN 向量检索 |
| `Veda.Storage/CosmosDbSemanticCache.cs` | 实现 `ISemanticCache`，语义缓存 |
| `Veda.Storage/CosmosDbServiceExtensions.cs` | `AddVedaCosmosDbStorage()` DI 注册 |
| `Veda.Services/LlmRouterService.cs` | 实现 `ILlmRouter`，路由 simple/advanced |
| `Veda.Services/DeepSeekChatService.cs` | DeepSeek OpenAI 兼容适配器 |
| `Veda.Core/Interfaces/ISemanticCache.cs` | 语义缓存接口 |
| `Veda.Core/Interfaces/ILlmRouter.cs` | LLM 路由接口 |
| `Veda.Api/Controllers/AdminController.cs` | DB 管理端点 |
| `Veda.Api/Middleware/ApiKeyMiddleware.cs` | API Key 认证中间件 |
| `infra/main.bicep`（或 `azure.yaml`） | Azure 基础设施 IaC |
| `.github/workflows/deploy.yml` | CI/CD（GitHub Actions → Container Apps） |

### 修改（现有文件）

| 文件 | 变更内容 |
|------|----------|
| `Veda.Services/ServiceCollectionExtensions.cs` | 支持 Ollama / AzureOpenAI 配置化注册 |
| `Veda.Services/QueryService.cs` | 集成 `ISemanticCache`、`ILlmRouter`；接收 `QueryMode` |
| `Veda.Services/DocumentIngestService.cs` | ingest 完成后触发 `ISemanticCache.InvalidateByDocumentAsync` |
| `Veda.Api/Controllers/QueryController.cs` | 增加 `mode` 参数（`QueryMode`） |
| `Veda.Api/Controllers/QueryStreamController.cs` | 增加 `mode` 参数 |
| `Veda.Api/Program.cs` | 注册新中间件、新服务、Rate Limiter |
| `Veda.Storage/ServiceCollectionExtensions.cs` | 支持 SQLite / CosmosDB 配置化切换 |
| `src/Veda.Api/appsettings.json` | 新增 LlmProvider、EmbeddingProvider、AzureOpenAI、DeepSeek、SemanticCache 配置节 |
| `Dockerfile` | 无需改动（Container Apps 直接使用） |
| `docker-compose.yml` | 新增 CosmosDB Emulator service（本地开发） |

---

## 5. 实施路线

### Sprint 1（1-2 周）：基础设施切换 ✅ 已完成

**目标**：云端能跑通一次完整的 RAG 查询。

- [x] `ServiceCollectionExtensions` 重构：Provider 配置化（Ollama ↔ Azure OpenAI）
- [x] `CosmosDbVectorStore` 实现 `IVectorStore`（含 DiskANN 向量索引配置）
- [x] `AddVedaCosmosDbStorage()` DI 扩展方法，支持 SQLite / CosmosDB 切换
- [x] `appsettings.json` 新增云端 Profile 配置节
- [ ] Container Apps 手工部署验证（`az containerapp up`）<!-- 操作验证步骤，需云端环境 -->
- [x] CosmosDB Emulator 集成到 `docker-compose.yml`（本地开发）

**验收**：本地（SQLite + Ollama）和云端（CosmosDB + Azure OpenAI）均可完成文档摄取 + 向量检索。

---

### Sprint 2（1-2 周）：LLM 路由 + 安全 + Dev 工具 ⚠️ 基本完成，2 项待处理

**目标**：双模式路由可用；API 安全可对外。

- [x] `ILlmRouter` + `LlmRouterService` 实现（DeepSeek 逻辑内联于 `LlmRouterService`，独立 `DeepSeekChatService` 未创建）
- [x] `QueryController` / `QueryStreamController` 增加 `mode` 参数
- [x] `QueryService` 集成 `ILlmRouter`
- [x] `ApiKeyMiddleware`（普通 Key + Admin Key 分离）
- [x] ASP.NET Core Rate Limiting（固定窗口，按 Key 计）
- [x] CORS 配置化
- [ ] MCP 端点认证：当前 `/mcp` 路径被 `ApiKeyMiddleware` **排除**在外，未受保护 ⚠️
- [x] `AdminController`（stats / clear / 分页查看）
- [ ] `Veda.Api.http` 更新 Admin 操作示例（仍为默认 weatherforecast 占位内容）

**验收**：
- `mode=simple` 走 GPT-4o-mini，`mode=advanced` 走 DeepSeek，日志可见
- 未携带 Key 的请求返回 HTTP 401
- 超频请求返回 HTTP 429
- Swagger UI 中可操作 Admin 端点清空数据库

---

### Sprint 3（1-2 周）：语义缓存 ⚠️ 基本完成，3 项偏差或待处理

**目标**：重复语义问题命中缓存，显著降低延迟与成本。

- [x] `ISemanticCache` 接口定义（实际签名为 `GetAsync` / `SetAsync` / `ClearAsync`，与设计文档描述的 `TryGetAsync` / `InvalidateByDocumentAsync` 有出入）
- [x] `CosmosDbSemanticCache` 实现（同步实现 `SqliteSemanticCache` 可用于本地）
- [x] `QueryService` 集成缓存查找与写入（缓存写入异步，不阻塞响应）
- [ ] `DocumentIngestService` 触发缓存失效：当前 `DocumentIngestService` 未注入 `ISemanticCache`，ingest 后不会主动清除缓存 ⚠️
- [x] `AdminController` 新增 `DELETE /api/admin/cache`
- [ ] `GET /api/admin/stats` 展示缓存命中率：stats 端点目前只返回 chunkCount / documentCount / syncedFileCount，无缓存相关统计 ⚠️
- [x] 缓存阈值可配置（`SemanticCacheOptions.SimilarityThreshold`，默认 0.95）

**验收**：
- 同义问题（相似度 ≥ 阈值）第二次请求响应 < 200ms
- Admin Stats 端点可见缓存命中计数
- Ingest 新文档后，相关缓存自动失效

---

### Sprint 4（1 周）：CI/CD + 收尾 ✅ 代码完成，端到端验证待云端

**目标**：稳定可展示，推送即部署。

- [x] Azure 基础设施 IaC（`infra/main.bicep` + `infra/modules/container-apps.bicep`）
- [x] Managed Identity：`DefaultAzureCredential` 已应用于 CosmosDB / Blob / Azure OpenAI 三处
- [x] GitHub Actions CI/CD（build → test → Docker push GHCR → deploy to Container Apps，使用 OIDC 联合身份）
- [x] 更新 `README.cn.md`：云端部署快速指南 + Admin API 端点说明
- [x] 更新 `docs/configuration/configuration.cn.md`：第 6-10 节覆盖全部二期配置项
- [ ] 验证 MCP 外部客户端端到端接入（需云端部署后实际测试）

**验收**：推送代码后 10 分钟内自动部署到 Container Apps，简历网站可通过 `/mcp` 端点完成知识检索。

---

## 6. 关键验收标准汇总

| 功能 | 验收标准 |
|------|----------|
| 云端部署 | API 运行于 Container Apps，冷启动 < 60 秒，可公开访问 |
| Azure OpenAI Embedding | 摄取后 CosmosDB 中可查到正确维度的向量 |
| 双模式路由 | `mode=advanced` 调用 DeepSeek，日志可见模型名称 |
| 语义缓存 | 相同语义问题第二次响应 < 200ms，Stats 端点可见命中计数 |
| 缓存失效 | 更新文档后，相关缓存被清除，下次查询走完整 RAG 管道 |
| API 安全 | 无 Key 请求 HTTP 401；超频请求 HTTP 429 |
| MCP 外部客户端 | 简历网站通过 `/mcp` 端点可调用 `search_knowledge_base` |
| DB Admin | 可通过 Swagger 清空 DB + 重新 ingest，全程 < 30 分钟 |
| Embedding 切换 | 修改配置 + 清库 + 重新 ingest，业务代码零修改 |
| Managed Identity | 云端运行时 appsettings 中无任何 Azure 密钥 |

---

## 7. 风险与注意事项

| 风险 | 应对策略 |
|------|----------|
| CosmosDB 向量索引配置 | 本地先用 CosmosDB Emulator（≥v2.14.9）验证，再部署云端 |
| Embedding 维度变更（1024 → 1536） | 云端属全新部署，无迁移问题；本地切换时清库重 ingest |
| DeepSeek API 网络延迟 | 国内直连稳定；如有问题可切换 Azure Marketplace 上的 DeepSeek |
| 语义缓存命中率不稳定 | 阈值配置化，可动态调整；可随时 `DELETE /api/admin/cache` 清除所有缓存 |
| Container Apps 冷启动 | Demo 前预热一次请求即可；如需要可设置 `minReplicas = 1`（略增成本） |
| API Key 管理 | 本地：User Secrets；云端：Container Apps 环境变量（绝不提交到代码库） |
| CosmosDB Serverless 向量索引 RU 消耗 | Serverless 的 ANN 查询 RU 较高，Demo 量级下可控；如超预算可降低 `topK` |

---

## 8. 与一期差距分析的对应关系

参考 [stage2-gap-analysis.cn.md](./stage2-gap-analysis.cn.md) P0 优先级项：

| 差距分析项 | 二期处理方式 |
|-----------|-------------|
| 基于任务特征的模型路由策略 | ✅ Sprint 2：`ILlmRouter` + `mode` API 参数 |
| MCP 跨系统上下文协议与知识互联 | ⚠️ Sprint 2：MCP 端点已公开，但 `/mcp` 路径当前排除在 API Key 认证之外 |
| 外部知识源动态接入（Blob） | ✅ 已有 `BlobStorageConnector`，云端直接启用 |
| 版本感知同步 | 部分满足：`SyncStateStore` 已有 Hash 比对；完整版本化暂缓至三期 |

P0 项中的富格式摄取（`Veda.Ingest.Layout`）、语义增强层（`Veda.Semantics`）等列为三期方向，不在二期范围内。

---

## 9. 未完成事项汇总（截至 2026-03-25）

> 基于对今日 4 个 commits（Sprint 1–4）的代码检查，以下事项尚未完成或实现与设计存在偏差。

### 9.1 待补全功能

| # | 归属 Sprint | 未完成项 | 影响 | 建议处理 |
|---|------------|---------|------|---------|
| 1 | Sprint 2 | **MCP 端点认证未启用**：`ApiKeyMiddleware.IsExcluded()` 中 `/mcp` 路径被豁免，外部调用无需 Key | 安全风险：MCP 工具（包括 `ingest_document`）无访问控制 | 从 `IsExcluded` 中移除 `/mcp`，或对 `ingest_document` 添加独立授权层 |
| 2 | Sprint 2 | **`Veda.Api.http` 未更新**：Admin / Query mode 示例仍为 weatherforecast 占位内容 | 开发体验问题，无法直接用 .http 文件调试新端点 | 补充 Admin 操作、`mode=simple/advanced` 示例请求 |
| 3 | Sprint 3 | **`DocumentIngestService` 不触发缓存失效**：ingest 后旧缓存可能持续返回过期答案 | 数据一致性问题 | 在 `DocumentIngestService` 注入 `ISemanticCache`，ingest 完成后调用 `ClearAsync()` 或增加按文档失效方法 |
| 4 | Sprint 3 | **Stats 端点不含缓存统计**：`/api/admin/stats` 只返回 chunk/文档/文件数，无缓存命中率 | 验收标准未达成 | 在 `ISemanticCache` 增加 `GetStatsAsync()` 或简单计数器，Stats 端点引用 |
| 5 | Sprint 4 | **云端端到端验收未完成**：Container Apps 手工部署验证 + MCP 外部客户端实测均依赖实际云端环境 | 最终验收标准未闭环 | 完成 Azure 资源配置和角色分配后执行 |

### 9.2 实现与设计文档的偏差（已落地但与原方案不同）

| 项目 | 设计文档描述 | 实际实现 | 影响评估 |
|------|------------|--------|---------|
| `DeepSeekChatService` | 独立文件 `Veda.Services/DeepSeekChatService.cs` | DeepSeek 逻辑内联在 `LlmRouterService` 中，复用 `OllamaChatService` 适配器 | 功能等价，代码更简洁；但若需要复用 DeepSeek 服务，需重构 |
| `ISemanticCache` 接口签名 | `TryGetAsync(float[], string hash)` + `SetAsync(string q, float[], string hash, RagQueryResponse)` + `InvalidateByDocumentAsync(string documentId)` | `GetAsync(float[])` + `SetAsync(float[], string answer)` + `ClearAsync()` | 简化实现，去掉 questionHash 参数和按文档失效；全局清除作为替代，粒度较粗 |
| `SemanticCache` TTL 配置键 | `Veda:Rag:SemanticCacheThreshold` | `Veda:SemanticCache:SimilarityThreshold` | 配置路径差异，文档需同步（已在 `configuration.cn.md` 中更新） |

