# VedaAide.NET

通用型、企业级、私有化 RAG 智能问答系统。

> English documentation: [README.md](README.md)

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
- [Node.js 22+](https://nodejs.org/)（前端）
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

> 所有文档均维护中英文两个版本：`.cn.md`（中文）和 `.en.md`（英文）。

---

## 主要 API 端点

| 方法 | 路径 | 说明 |
|------|------|------|
| `POST` | `/api/documents` | 摄取文档到知识库 |
| `POST` | `/api/documents/upload` | 上传图片/PDF（多模态）到知识库 |
| `POST` | `/api/query` | RAG 问答（返回答案 + 来源 + 幻觉标记、支持 structuredOutput） |
| `GET` | `/api/querystream` | SSE 流式 RAG 问答 |
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

当前测试数量：**134 个测试**，全部通过。

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
| 阮段六 | AI 评估体系（忠实度、相关性、A/B 测试） | ✅ 已完成 |
| 三期 Sprint 1 | 知识作用域（KnowledgeScope）+ 混合检索双通道（RRF 融合） | ✅ 已完成 |
| 三期 Sprint 2 | 富格式文档摄取（Document Intelligence OCR + Vision 多模态） | ✅ 已完成 |
| 三期 Sprint 3 | 结构化推理输出 + 知识版本化 + 语义增强层（个人词库） | ✅ 已完成 |
| 三期 Sprint 4 | 隐式反馈学习 + 多用户知识治理（四层隔离模型） | ✅ 已完成 |
