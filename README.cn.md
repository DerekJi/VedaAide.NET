# VedaAide.NET

> **生产级 RAG 系统** — 基于 C# / .NET 10 + Semantic Kernel 从零构建。  
> 面向企业私有部署：混合检索、多层防幻觉、Agent 编排、MCP 集成、定量评估框架。

> English documentation: [README.md](README.md)

---

## 为什么我构建了这个

大多数 RAG 教程到 "Embed → Store → Search → Answer" 就停了。但那还不够生产级。  
这个项目是我对这个问题的回答：*一个真正可用于生产的 RAG 系统到底应该是什么样的？*

每一项架构决策都是有意为之 — 并在[架构决策记录](docs/rag-internals/09-adr.cn.md)中有详细文档。

---

## 架构一览

```
┌─────────────────────────────────────────────────────────────────┐
│  入口点：REST + GraphQL + SSE + MCP HTTP                        │
├─────────────────────────────────────────────────────────────────┤
│  Agent 层：ReAct Agent (SK 插件) · OrchestrationService        │
│  评估层：忠实度 · 回答相关性 · 上下文召回                      │
│  MCP 服务：search_knowledge_base · ingest · list_documents     │
├─────────────────────────────────────────────────────────────────┤
│  核心服务：                                                     │
│  DocumentIngestService ──► 分块 → Embedding → 去重 → 存储      │
│  QueryService          ──► HybridRetriever → ContextWindow     │
│                             → LlmRouter → HallucinationGuard   │
│  EmbeddingService · LlmRouter · SemanticCache                  │
├─────────────────────────────────────────────────────────────────┤
│  存储层：CosmosDB (DiskANN) · SQLite-VSS                        │
│          SemanticCache · UserMemoryStore · SyncStateStore      │
└─────────────────────────────────────────────────────────────────┘
```

八个分层 C# 项目，严格的依赖方向：`Core → Services → Storage → Entry Points`。  
详见[完整模块依赖图](docs/rag-internals/06-module-dependencies.cn.md)。

---

## 关键工程决策

### 1. 混合检索 + RRF 融合
密集向量检索（余弦相似度）和稀疏关键词检索并发运行。  
结果通过 **Reciprocal Rank Fusion (RRF, k=60)** 数学公式融合 — 无需调参，理论上最优。  
同时支持 `WeightedSum` 和 `RRF` 两种融合策略，可配置。

> *为什么不只用向量检索？* 关键词检索在精确匹配、产品编码、专有名词上远超向量检索。混合方案弥补两种方法的各自缺陷。

### 2. 双层防幻觉
- **第一层 — 自检：** LLM 在一次调用中生成答案 + 置信度标志（Structured Output）
- **第二层 — Guard 验证：** `HallucinationGuardService` 将答案 + 检索到的上下文发给独立的 LLM 调用做二次事实核查

可通过 `Veda:Rag:EnableSelfCheckGuard` 配置。额外耗时 ~300ms，但能消除无依据的声明。

### 3. 语义缓存（CosmosDB + SQLite）
任何新问题到达前，先与缓存中的问题 Embedding 通过余弦相似度做匹配。  
缓存命中阈值可配置（`SemanticCacheOptions:SimilarityThreshold`）。  
提供两个实现：`CosmosDbSemanticCache`（生产环境）和 `SqliteSemanticCache`（本地/开发）。

### 4. LLM 路由器
`LlmRouterService` 根据 `QueryMode` 选择合适的模型：
- `Simple` → 轻量级模型（Ollama 本地 / GPT-4o-mini）
- `Advanced` → DeepSeek R2（或任何 OpenAI 兼容的端点）

优雅降级：若 Advanced 模型未配置，自动路由到 Simple。

### 5. Token 预算感知的上下文窗口
`ContextWindowBuilder` 按相似度得分选择 chunk，同时强制执行严格的 token 预算（保守估算 3 字符/token 以处理中英混合内容）。  
防止低相关性 chunk 填满 LLM 上下文窗口。

### 6. ReAct Agent（Semantic Kernel 插件）
`VedaKernelPlugin` 将知识库检索暴露为 `[KernelFunction]`。  
SK 的 `ChatCompletionAgent` 在 **Reason-Act-Observe** 循环中使用 — Agent 自主决策*何时*和*检索什么*，而不是硬编码在查询路径上。

### 7. MCP Server
VedaAide 通过 **Model Context Protocol (HTTP 传输)** 暴露三个工具：
- `search_knowledge_base` — 语义搜索向量库
- `list_documents` — 浏览已摄取文档
- `ingest_document` — 在运行时添加内容

接入 VS Code Copilot、Claude Desktop 或任何 MCP 兼容的 AI 助手，只需一行配置。

### 8. 定量 RAG 评估
三个评分器，各自使用 LLM-as-a-Judge 方法：

| 指标 | 衡量内容 |
|-----|---------|
| **忠实度** | 答案中的每项主张都能从检索的上下文中找到支持 |
| **回答相关性** | 答案确实在解答所提出的问题 |
| **上下文召回** | 检索的 chunk 包含回答问题所需的全部信息 |

评分被存储、可通过 `/api/evaluation` 查询，支持不同检索策略的 A/B 对比。

---

## 技术栈

| 层级 | 技术 | 备注 |
|------|------|------|
| 后端 | .NET 10、ASP.NET Core | 清晰架构，8 个项目 |
| AI 编排 | Semantic Kernel 1.73 | 插件式 ReAct Agent |
| 向量数据库 | Azure CosmosDB (DiskANN) / SQLite-VSS | 通过 `IVectorStore` 可插拔 |
| LLM / Embedding | Ollama（本地）、Azure OpenAI、DeepSeek | 多模型路由 |
| API | REST + GraphQL (HotChocolate 15) + SSE | 支持流式问答 |
| MCP | ModelContextProtocol.AspNetCore | HTTP 传输 |
| 前端 | Angular 19（Standalone + Signals） | 实时 SSE 流式 UI |
| 认证 | Azure Entra External ID (CIAM) | 基于 JWT 的用户数据隔离 |
| 可观测性 | OpenTelemetry | 结构化日志 + 健康检查 |
| 部署 | Docker Compose（本地）/ Azure Container Apps | `/infra` 中有 Bicep IaC |

---

## 快速启动

### 前置要求

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Ollama](https://ollama.com/)，并下载所需模型：
  ```bash
  ollama pull bge-m3        # 向量模型
  ollama pull qwen3:8b      # 对话模型
  ```
- [Node.js 24+](https://nodejs.org/) 用于前端
- [Docker](https://www.docker.com/) 用于容器化部署

### 本地运行

```bash
# 1. 启动 Ollama
ollama serve

# 2. 启动 API
cd src/Veda.Api && dotnet run

# 3. 启动前端（新开终端）
cd src/Veda.Web && npm install && npm start
```

| 端点 | URL |
|------|-----|
| API | http://localhost:5126 |
| 前端 | http://localhost:4200 |
| Swagger | http://localhost:5126/swagger |
| GraphQL Playground | http://localhost:5126/graphql |
| MCP | http://localhost:5126/mcp |

### Docker Compose

```bash
docker compose up -d
# 可选：通过 Cloudflare Tunnel 对外暴露
docker compose --profile tunnel up -d
```

---

## 项目结构

```
VedaAide.NET/
├── src/
│   ├── Veda.Core/          # 领域模型、所有 IXxx 接口、配置选项
│   ├── Veda.Services/      # RAG 引擎：摄取、检索、Embedding、LLM 路由
│   ├── Veda.Storage/       # EF Core、向量存储、语义缓存、同步状态
│   ├── Veda.Prompts/       # 上下文窗口构建器、思维链策略
│   ├── Veda.Agents/        # Semantic Kernel ReAct Agent、编排服务
│   ├── Veda.MCP/           # MCP 服务器工具
│   ├── Veda.Evaluation/    # 忠实度 / 相关性 / 召回率 评分器
│   ├── Veda.Api/           # ASP.NET Core：REST + GraphQL + SSE + MCP
│   └── Veda.Web/           # Angular 19 前端
├── tests/
│   ├── Veda.Core.Tests/
│   └── Veda.Services.Tests/ # 167 个测试，全部通过
├── docs/
│   ├── rag-internals/      # 9 张 PlantUML 架构图
│   ├── designs/            # 各阶段设计文档 + ADR
│   └── insights/           # 工程决策记录
├── infra/                  # Azure Bicep IaC
└── docker-compose.yml
```

---

## API 参考（代表性）

| 方法 | 路径 | 说明 |
|------|------|------|
| `POST` | `/api/documents` | 摄取文档 |
| `POST` | `/api/documents/upload` | 上传 PDF / 图片（多模态 OCR） |
| `POST` | `/api/query` | RAG 问答 → 答案 + 来源 + 幻觉标记 |
| `GET` | `/api/querystream` | 流式 RAG (SSE) |
| `POST` | `/api/orchestrate/query` | Agent 编排问答（ReAct 循环） |
| `POST` | `/api/datasources/sync` | 触发数据源同步（Blob / 文件系统） |
| `POST` | `/api/feedback` | 上报接受 / 拒绝 / 编辑反馈 |
| `POST` | `/api/governance/groups` | 创建知识共享组 |
| `POST` | `/api/evaluation/run` | 运行 RAG 评估框架 |
| `GET` | `/api/evaluation/reports` | 查询评估结果 |
| `POST` | `/mcp` | MCP 端点（VS Code Copilot / Claude Desktop） |
| `POST` | `/graphql` | GraphQL 端点 |

完整 API：运行本地时见 [Swagger](http://localhost:5126/swagger)。

---

## 运行测试

```bash
dotnet test                                         # 所有 167 个测试
dotnet test --filter "Category!=Integration"        # 仅单元测试
dotnet test --collect:"XPlat Code Coverage"         # 含覆盖率
./scripts/smoke-test.sh                             # 烟雾测试（API 需运行）
```

---

## MCP 集成

API 运行时，将下面配置加到 `.vscode/mcp.json`：

```json
{
  "servers": {
    "vedaaide": {
      "type": "http",
      "url": "http://localhost:5126/mcp"
    }
  }
}
```

可用工具：`search_knowledge_base` · `list_documents` · `ingest_document`

---

## 文档

| 文档 | 说明 |
|------|------|
| [系统架构](docs/rag-internals/01-system-architecture.cn.md) | 分层图 + Azure 基础设施 |
| [摄取流程](docs/rag-internals/02-ingest-flow.cn.md) | 分块 → Embedding → 去重 → 版本化 |
| [查询流程](docs/rag-internals/03-query-flow.cn.md) | 混合检索 → RRF → 上下文窗口 → 防幻觉 |
| [存储与检索](docs/rag-internals/04-storage-retrieval.cn.md) | SQLite vs CosmosDB、语义缓存 |
| [RAG 概念↔代码对照](docs/rag-internals/05-concept-code-map.cn.md) | 30 个标准 RAG 术语映射到实现 |
| [架构决策记录](docs/rag-internals/09-adr.cn.md) | 7 条关键决策及理由 |
| [配置参考](docs/configuration/configuration.cn.md) | 所有 `appsettings` 键与环境变量 |
| [Azure 部署](docs/rag-internals/08-azure-deployment.cn.md) | Container Apps + CosmosDB + CI/CD |

> 所有文档均维护中英文两个版本：`.cn.md`（中文）和 `.en.md`（英文）。

---

## 实现进展

| 阶段 | 说明 | 状态 |
|------|------|------|
| Phase 0 | 方案框架、EF Core、DI 容器 | ✅ |
| Phase 1 | 核心 RAG：摄取 + 向量检索 + LLM 问答 | ✅ |
| Phase 2 | RAG 质量：去重 + 防幻觉 | ✅ |
| Phase 3 | 全栈：GraphQL + SSE 流式 + Angular + Docker | ✅ |
| Phase 4 | Agent 工作流 + MCP Server + Prompt 工程 | ✅ |
| Phase 5 | 外部数据源（文件系统 + Blob）、后台同步 | ✅ |
| Phase 6 | 评估框架：忠实度、相关性、A/B 测试 | ✅ |
| Stage 3.1 | KnowledgeScope + 混合检索（RRF 融合） | ✅ |
| Stage 3.2 | 富文档提取：Document Intelligence OCR + Vision 多模态 | ✅ |
| Stage 3.3 | 结构化推理输出 + 知识版本化 + 语义增强器 | ✅ |
| Stage 3.4 | 隐式反馈学习 + 多用户知识治理（4 层模型） | ✅ |
| Stage 5 | Azure Entra External ID CIAM + JWT 用户数据隔离 | ✅ |
| Stage 6 | Token 统计、邮件摄取 (EML/MSG)、管理员角色隔离 | ✅ |
| Stage 7 | Context Augmentation：无 DB 写入的临时文件/图片注入 | ✅ |


---

## 项目简介

VedaAide.NET 是基于 .NET 10 和 Semantic Kernel 构建的全栈 AI 知识库系统，支持本地文档摄取、语义检索、带防幻觉校验的 LLM 问答、MCP（模型上下文协议）集成和 Agent 自主编排工作流。

**核心特性：**
- 私有化部署 — 通过 Ollama 完全本地运行，无需云端 API
- 通过 Server-Sent Events 实现流式问答
- 双层去重 + 双层防幻觉检测
- IRCoT（交替检索与思维链推理）驱动的 Agent 编排
- MCP Server（对外暴露） + MCP Client（Azure Blob / 文件系统数据源）
- 基于内容哈希的增量同步 — 自动跳过未变更的文件
- Angular 19 前端，实时流式 UI

---

## 技术栈

| 层级 | 技术 |
|------|------|
| 后端 | .NET 10、ASP.NET Core、EF Core 10 + SQLite |
| AI 编排 | Semantic Kernel 1.73 |
| LLM / Embedding | Ollama（本地）、DeepSeek / Azure OpenAI（云端） |
| API | GraphQL（HotChocolate 15）+ REST + SSE |
| 前端 | Angular 19（Standalone + Signals API） |
| MCP | ModelContextProtocol.AspNetCore（HTTP 传输） |
| 认证 | Azure Entra External ID (CIAM) + MSAL Angular 3 |
| 云服务 | Azure Blob Storage、Azure Container Apps |
| 部署 | Docker Compose（本地）/ Azure Container Apps（云端） |

---

## 快速启动

### 前置要求

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Ollama](https://ollama.com/)，并下载所需模型：
  ```bash
  ollama pull bge-m3        # Embedding 模型
  ollama pull qwen3:8b      # 对话模型
  ```
- [Node.js 24+](https://nodejs.org/)（前端）
- [Docker](https://www.docker.com/)（容器化部署）

### 本地运行

```bash
# 启动 Ollama（若未作为系统服务运行）
ollama serve

# 启动 API
cd src/Veda.Api
dotnet run

# 启动前端（另开终端）
cd src/Veda.Web
npm install
npm start
```

API：http://localhost:5126  
前端：http://localhost:4200  
GraphQL Playground：http://localhost:5126/graphql  
Swagger：http://localhost:5126/swagger

### Docker Compose 运行

```bash
docker compose up -d
```

启动：`veda-api` + `veda-web` + `ollama` 三个服务（`cloudflared` 默认不启动，如需对外暴露本地服务可加 `--profile tunnel`）。

---

## 项目结构

```
VedaAide.NET/
├── src/
│   ├── Veda.Core/          # 领域模型、接口、共享工具
│   ├── Veda.Services/      # RAG 引擎、Embedding、LLM、数据源 Connector
│   ├── Veda.Storage/       # EF Core DbContext、向量存储、同步状态
│   ├── Veda.Prompts/       # Prompt 模板、上下文窗口构建器、CoT 策略
│   ├── Veda.Agents/        # LLM 编排（Semantic Kernel Agent）
│   ├── Veda.MCP/           # MCP Server 工具（知识库 + 摄取）
│   └── Veda.Api/           # ASP.NET Core API（REST + GraphQL + SSE + MCP）
├── tests/
│   ├── Veda.Core.Tests/
│   └── Veda.Services.Tests/
├── docs/
│   ├── configuration/      # 配置说明文档
│   ├── designs/            # 架构与各阶段设计文档
│   ├── insights/           # 工程洞察与设计决策
│   └── tests/              # 测试策略与规范
├── cloudflare/             # Cloudflare Tunnel 配置
└── docker-compose.yml
```

---

## 文档目录

| 文档 | 说明 |
|------|------|
| [docs/configuration/configuration.cn.md](docs/configuration/configuration.cn.md) | 完整配置项说明（appsettings、环境变量、User Secrets） |
| [infra/README.md](infra/README.md) | Azure Bicep IaC 快速部署指南 |
| [.github/workflows/deploy.yml](.github/workflows/deploy.yml) | GitHub Actions CI/CD 流程 |
| [docs/designs/system-design.cn.md](docs/designs/system-design.cn.md) | 架构概览与各阶段技术路线图 |
| [docs/designs/phase4-mcp-agents.cn.md](docs/designs/phase4-mcp-agents.cn.md) | 阶段四/五设计：MCP、Agent 编排、Prompt 工程 |
| [docs/tests/README.cn.md](docs/tests/README.cn.md) | 测试体系总览 |
| [docs/tests/test-conventions.cn.md](docs/tests/test-conventions.cn.md) | 测试命名规范与编写约定 |
| [docs/insights/README.cn.md](docs/insights/README.cn.md) | 工程洞察索引 |
| [cloudflare/README.md](cloudflare/README.md) | Cloudflare Tunnel 配置指南 |
| **RAG 内部机制（PlantUML 架构图）** | |
| [docs/rag-internals/PLAN.cn.md](docs/rag-internals/PLAN.cn.md) | RAG 内部文档索引 |
| [docs/rag-internals/01-system-architecture.cn.md](docs/rag-internals/01-system-architecture.cn.md) | 系统架构：6 个项目分层 + Azure 基础设施（PlantUML） |
| [docs/rag-internals/02-ingest-flow.cn.md](docs/rag-internals/02-ingest-flow.cn.md) | Ingest 流程：分块、Embedding、去重、版本化（PlantUML） |
| [docs/rag-internals/03-query-flow.cn.md](docs/rag-internals/03-query-flow.cn.md) | Query 流程：混合检索、Rerank、CoT、防幻觉（PlantUML） |
| [docs/rag-internals/04-storage-retrieval.cn.md](docs/rag-internals/04-storage-retrieval.cn.md) | 存储层：SQLite vs CosmosDB、向量检索、语义缓存（PlantUML） |
| [docs/rag-internals/05-concept-code-map.cn.md](docs/rag-internals/05-concept-code-map.cn.md) | RAG 概念↔代码对照表（30 个标准术语） |
| [docs/rag-internals/06-module-dependencies.cn.md](docs/rag-internals/06-module-dependencies.cn.md) | 模块依赖拓扑 + DI 注册（PlantUML） |
| [docs/rag-internals/07-data-model-er.cn.md](docs/rag-internals/07-data-model-er.cn.md) | 数据模型 ER 图：所有实体与关系（PlantUML） |
| [docs/rag-internals/08-azure-deployment.cn.md](docs/rag-internals/08-azure-deployment.cn.md) | Azure 部署：Container Apps、CosmosDB、CI/CD（PlantUML） |
| [docs/rag-internals/09-adr.cn.md](docs/rag-internals/09-adr.cn.md) | 架构决策记录：7 条关键决策 |

> 所有文档均维护中英文两个版本：`.cn.md`（中文）和 `.en.md`（英文）。

---

## 主要 API 端点

| 方法 | 路径 | 说明 |
|------|------|------|
| `POST` | `/api/documents` | 摄取文档到知识库 |
| `POST` | `/api/documents/upload` | 上传图片/PDF（多模态）到知识库 |
| `POST` | `/api/query` | RAG 问答（返回答案 + 来源 + 幻觉标记、支持 structuredOutput） |
| `GET` | `/api/querystream` | SSE 流式 RAG 问答 |
| `POST` | `/api/querystream` | 带临时附件上下文的流式 RAG 问答（Context Augmentation） |
| `POST` | `/api/context/extract` | 从上传文件提取文本（临时，不写数据库） |
| `POST` | `/api/orchestrate/query` | Agent 编排问答 |
| `POST` | `/api/orchestrate/ingest` | Agent 编排摄取 |
| `POST` | `/api/datasources/sync` | 手动触发所有已启用数据源同步 |
| `POST` | `/api/feedback` | 上报用户行为事件（撇取/拒绝/编辑/点击） |
| `GET` | `/api/feedback/stats` | 查看高频被标记无关的 chunk 列表 |
| `POST` | `/api/governance/groups` | 创建共享组（家庭/团队） |
| `PUT` | `/api/governance/documents/{id}/share` | 授权文档共享 |
| `GET` | `/api/governance/consensus/pending` | 列出待审核共识候选 |
| `POST` | `/api/governance/consensus/{id}/review` | 审核共识候选（管理员） |
| `GET` | `/api/governance/documents/{id}/visible` | 文档对用户的可见性隔离检查 |
| `GET` | `/api/admin/stats` | 查看向量库统计信息（Admin Key 必填） |
| `GET` | `/api/admin/chunks` | 分页浏览向量块（Admin Key 必填） |
| `GET` | `/api/admin/documents/{name}/history` | 查看文档版本历史 |
| `DELETE` | `/api/admin/data` | 清空所有向量数据（需 `X-Confirm: yes`） |
| `DELETE` | `/api/admin/cache` | 清空语义缓存 |
| `DELETE` | `/api/admin/documents/{id}` | 删除指定文档 |
| `GET` | `/api/prompts` | 获取 Prompt 模板列表 |
| `POST` | `/api/prompts` | 创建/更新 Prompt 模板 |
| `POST` | `/mcp` | MCP 端点（供 VS Code Copilot 等 MCP 客户端连接） |
| `POST` | `/graphql` | GraphQL 端点 |

---

## 配置

完整说明见 [docs/configuration/configuration.cn.md](docs/configuration/configuration.cn.md)。

`src/Veda.Api/appsettings.json` 核心配置项：

```json
{
  "Veda": {
    "OllamaEndpoint": "http://localhost:11434",
    "EmbeddingModel": "bge-m3",
    "ChatModel": "qwen3:8b",
    "DataSources": {
      "FileSystem": { "Enabled": false, "Path": "" },
      "BlobStorage": { "Enabled": false, "ContainerName": "" },
      "AutoSync":    { "Enabled": false, "IntervalMinutes": 60 }
    }
  }
}
```

敏感信息（如 `BlobStorage:ConnectionString`）应通过 User Secrets 或环境变量设置，不应写入 `appsettings.json`。

---

## 运行测试

```bash
# 所有单元测试
dotnet test

# 带覆盖率统计
dotnet test --collect:"XPlat Code Coverage"

# 冒烟测试（需 API 启动）
./scripts/smoke-test.sh
```

当前测试数量：**167 个测试**，全部通过。

---

## 云端部署（Azure Container Apps）

### 前置要求

- Azure 订阅
- Azure CLI (`az`) 或 Azure Developer CLI (`azd`)
- GitHub Actions Secrets 已配置（见下文）
- Azure OpenAI 资源（Embedding + Chat 部署）
- Azure CosmosDB for NoSQL 账户（启用向量搜索功能）

### 基础设施部署（Bicep）

```bash
# 登录 Azure
az login

# 部署所有资源（Container Apps Environment + Container App + Managed Identity）
az deployment group create \
  --resource-group dev-dj-sbi-customer_group \
  --template-file infra/main.bicep \
  --parameters @infra/main.parameters.local.json
```

> 部署完成后复制输出的 `identityPrincipalId` 用于后续授权。

### Managed Identity 授权

```bash
# 获取 Managed Identity 的 Principal ID（Bicep 输出中可得）
PRINCIPAL_ID="<identityPrincipalId>"

# 授权访问 Azure OpenAI
az role assignment create \
  --assignee "$PRINCIPAL_ID" \
  --role "Cognitive Services OpenAI User" \
  --scope "/subscriptions/<SUB>/resourceGroups/dev-dj-sbi-customer_group/providers/Microsoft.CognitiveServices/accounts/<AOAI_NAME>"

# 授权访问 CosmosDB（内置 Data Contributor 角色）
az cosmosdb sql role assignment create \
  --account-name <COSMOS_ACCOUNT> \
  --resource-group dev-dj-sbi-customer_group \
  --principal-id "$PRINCIPAL_ID" \
  --role-definition-id 00000000-0000-0000-0000-000000000002 \
  --scope "/"
```

授权后，`appsettings.json` 中 `AzureOpenAI:ApiKey` 和 `CosmosDb:AccountKey` 保持为空，系统自动使用 Managed Identity 认证。

### GitHub Actions CI/CD

在 GitHub 仓库中配置以下 Secrets / Variables：

| 类型 | 名称 | 说明 |
|------|------|------|
| Secret | `AZURE_CLIENT_ID` | 用于 GitHub Actions Federated Identity 的应用注册客户端 ID |
| Secret | `AZURE_TENANT_ID` | Azure AD 租户 ID |
| Secret | `AZURE_SUBSCRIPTION_ID` | Azure 订阅 ID |
| Variable | `AZURE_RESOURCE_GROUP` | 资源组名（如 `dev-dj-sbi-customer_group`） |
| Variable | `CONTAINER_APP_NAME` | Container App 名（如 `vedaaide-dev-api`） |

配置 Federated Identity（允许 GitHub Actions 免密登录 Azure）：

```bash
az ad app federated-credential create \
  --id <APP_OBJECT_ID> \
  --parameters '{
    "name": "github-deploy",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:YOUR_ORG/VedaAide.NET:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

推送到 `main` 分支后，CI/CD 自动完成：构建 → 测试 → 推送镜像 → 部署到 Container Apps，全程约 5-8 分钟。

### 云端配置（通过环境变量）

Container Apps 中无需修改代码，通过环境变量切换后端：

```bash
# 切换到 CosmosDB + AzureOpenAI
az containerapp update --name vedaaide-dev-api --resource-group dev-dj-sbi-customer_group \
  --set-env-vars \
    "Veda__StorageProvider=CosmosDb" \
    "Veda__CosmosDb__Endpoint=https://YOUR_COSMOS.documents.azure.com:443/" \
    "Veda__EmbeddingProvider=AzureOpenAI" \
    "Veda__LlmProvider=AzureOpenAI" \
    "Veda__AzureOpenAI__Endpoint=https://YOUR_AOAI.openai.azure.com/" \
    "Veda__SemanticCache__Enabled=true"
```

---

## 运行测试

API 运行时，在 `.vscode/mcp.json` 中添加：

```json
{
  "servers": {
    "vedaaide": {
      "type": "http",
      "url": "http://localhost:5126/mcp"
    }
  }
}
```

可用工具：`search_knowledge_base`、`list_documents`、`ingest_document`

---

## 实现阶段

| 阶段 | 说明 | 状态 |
|------|------|------|
| 阶段零 | Solution 脚手架、EF Core、DI 配置 | ✅ 已完成 |
| 阶段一 | 基础 RAG 引擎（摄取 + 向量检索 + LLM 问答） | ✅ 已完成 |
| 阶段二 | RAG 质量增强（去重 + 防幻觉 + Reranking） | ✅ 已完成 |
| 阶段三 | 全栈（GraphQL + SSE 流式 + Angular 前端 + Docker） | ✅ 已完成 |
| 阶段四 | Agentic 工作流 + MCP Server + Prompt 工程 | ✅ 已完成 |
| 阶段五 | 外部数据源（FileSystem + Blob）、后台同步、同步状态追踪 | ✅ 已完成 |
| 二期 Sprint 1 | CosmosDB 向量存储 + AzureOpenAI Embedding/Chat 提供商切换 | ✅ 已完成 |
| 二期 Sprint 2 | LLM 双模式路由（DeepSeek Advanced）+ API 安全 + Admin 管理端点 | ✅ 已完成 |
| 二期 Sprint 3 | 语义缓存（ISemanticCache，SQLite + CosmosDB 双后端） | ✅ 已完成 |
| 二期 Sprint 4 | CI/CD（GitHub Actions）+ Bicep IaC + Managed Identity | ✅ 已完成 |
| 阶段六 | AI 评估体系（忠实度、相关性、A/B 测试） | ✅ 已完成 |
| 三期 Sprint 1 | 知识作用域（KnowledgeScope）+ 混合检索双通道（RRF 融合） | ✅ 已完成 |
| 三期 Sprint 2 | 富格式文档摄取（Document Intelligence OCR + Vision 多模态） | ✅ 已完成 |
| 三期 Sprint 3 | 结构化推理输出 + 知识版本化 + 语义增强层（个人词库） | ✅ 已完成 |
| 三期 Sprint 4 | 隐式反馈学习 + 多用户知识治理（四层隔离模型） | ✅ 已完成 |
| 五期 | 用户身份认证（Azure Entra External ID CIAM）+ 全路由保护（MsalGuard）+ JWT 用户数据隔离 | ✅ 已完成 |
| 六期 | 富格式文档提取质量提升（Certificate 类型、PDF 文字层、Azure DI 配额感知、Ollama Vision 提供商、Token 消耗统计、邮件摄取 EML/MSG、管理员角色隔离） | ✅ 已完成 |
| 七期 Phase 4 | Context Augmentation（临时 RAG）：Chat 中上传文件/粘贴截图，提取文本不写数据库，直接注入 LLM Prompt | ✅ 已完成 |
