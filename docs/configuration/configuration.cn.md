# VedaAide.NET 配置说明

本文档系统说明 `appsettings.json`、环境变量及 User Secrets 的所有可配置项。

---

## 目录

1. [核心设置](#1-核心设置)
2. [RAG 参数](#2-rag-参数)
3. [数据源：文件系统](#3-数据源文件系统)
4. [数据源：Azure Blob Storage](#4-数据源azure-blob-storage)
5. [数据源：自动同步](#5-数据源自动同步)
6. [存储后端切换（二期新增）](#6-存储后端切换二期新增)
7. [AI 提供商切换（二期新增）](#7-ai-提供商切换二期新增)
8. [DeepSeek 高级推理（二期新增）](#8-deepseek-高级推理二期新增)
9. [API 安全（二期新增）](#9-api-安全二期新增)
10. [语义缓存（二期新增）](#10-语义缓存二期新增)
11. [混合检索（三期新增）](#11-混合检索三期新增)
12. [富格式文档摄取（三期新增）](#12-富格式文档摄取三期新增)
13. [语义增强层（三期新增）](#13-语义增强层三期新增)
14. [User Secrets（开发环境敏感参数）](#14-user-secrets开发环境敏感参数)
15. [环境变量覆盖](#15-环境变量覆盖)
16. [配置优先级](#16-配置优先级)

---

## 1. 核心设置

配置节：`Veda`

```json
{
  "Veda": {
    "OllamaEndpoint": "http://localhost:11434",
    "EmbeddingModel": "bge-m3",
    "ChatModel": "qwen3:8b",
    "DbPath": "veda.db"
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `OllamaEndpoint` | string | `http://localhost:11434` | Ollama 服务地址，本地运行时通常无需修改 |
| `EmbeddingModel` | string | `bge-m3` | Ollama Embedding 模型名称。`bge-m3`（1024 维，中英日多语）推荐用于中文内容；`nomic-embed-text`（768 维，英文轻量）适合纯英文场景。**切换模型后必须清空向量库并重新摄取文档（维度不兼容）。** |
| `ChatModel` | string | `qwen3:8b` | Ollama 对话模型名称，可换为任意 Ollama 已下载的模型 |
| `DbPath` | string | `veda.db` | SQLite 数据库文件路径（相对于 API 工作目录）。Docker 部署时应挂载到宿主机 Volume |

---

## 2. RAG 参数

配置节：`Veda:Rag`

```json
{
  "Veda": {
    "Rag": {
      "SimilarityDedupThreshold": 0.95,
      "HallucinationSimilarityThreshold": 0.3,
      "EnableSelfCheckGuard": false
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `SimilarityDedupThreshold` | float | `0.95` | 摄取阶段向量相似度去重阈值。新分块与已存储内容余弦相似度 ≥ 该值则跳过，视为语义重复。`0.95` 是保守值，适合大多数场景；高度标准化文档（如法规条文）可降至 `0.90` |
| `HallucinationSimilarityThreshold` | float | `0.3` | 防幻觉第一层阈值。LLM 回答的 Embedding 与向量库最高相似度 < 该值时，标记回答为潜在幻觉（`IsHallucination = true`）。不会拦截回答，仅做标记 |
| `EnableSelfCheckGuard` | bool | `false` | 是否启用防幻觉第二层（LLM 自我校验）。开启后会对每次回答额外发起一次 LLM 调用进行事实核查，成本较高，建议仅在高合规要求场景启用 |

---

## 3. 数据源：文件系统

配置节：`Veda:DataSources:FileSystem`

```json
{
  "Veda": {
    "DataSources": {
      "FileSystem": {
        "Enabled": false,
        "Path": "/data/documents",
        "Extensions": [".txt", ".md"]
      }
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | bool | `false` | 是否启用本地文件系统数据源 |
| `Path` | string | `""` | 要监控的本地目录绝对路径。Docker 中应填容器内路径（配合 Volume 挂载使用） |
| `Extensions` | string[] | `[".txt", ".md"]` | 允许摄取的文件扩展名白名单，其他格式的文件会被跳过 |

**同步行为：** 每次同步仅处理内容哈希发生变化的文件（基于 SHA-256），内容未变的文件自动跳过，无需重复摄取。

---

## 4. 数据源：Azure Blob Storage

配置节：`Veda:DataSources:BlobStorage`

```json
{
  "Veda": {
    "DataSources": {
      "BlobStorage": {
        "Enabled": false,
        "ConnectionString": "",
        "AccountUrl": "",
        "ContainerName": "my-docs",
        "Prefix": "",
        "Extensions": [".txt", ".md"]
      }
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | bool | `false` | 是否启用 Azure Blob Storage 数据源 |
| `ConnectionString` | string | `""` | Blob Storage 连接字符串（包含 AccountName + Key）。与 `AccountUrl` **二选一**，优先使用 `ConnectionString` |
| `AccountUrl` | string | `""` | Blob Storage 账户 URL，格式为 `https://<account>.blob.core.windows.net`。适用于 Managed Identity 或 `az login` 认证（无密钥场景）。`ConnectionString` 非空时此字段被忽略 |
| `ContainerName` | string | `""` | 要同步的容器名称，**必填** |
| `Prefix` | string | `""` | Blob 路径前缀过滤器。Azure 中路径以 `/` 分隔模拟目录层级，如 `docs/` 表示只同步 `docs/` "目录"下的 Blob；留空则同步整个容器。过滤在服务端完成，效率高 |
| `Extensions` | string[] | `[".txt", ".md"]` | 允许摄取的文件扩展名白名单 |

**认证配置示例：**

```json
// 方式一：连接字符串（含密钥）
"ConnectionString": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=xxx;EndpointSuffix=core.windows.net"

// 方式二：Managed Identity / az login（无密钥）
"AccountUrl": "https://myaccount.blob.core.windows.net"
// ConnectionString 留空即可
```

**同步行为：** 基于内容 SHA-256 哈希跳过未变更的 Blob，每次同步只处理新增或内容变更的文件。

---

## 5. 数据源：自动同步

配置节：`Veda:DataSources:AutoSync`

```json
{
  "Veda": {
    "DataSources": {
      "AutoSync": {
        "Enabled": false,
        "IntervalMinutes": 60
      }
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Enabled` | bool | `false` | 是否启用后台自动同步服务（`DataSourceSyncBackgroundService`） |
| `IntervalMinutes` | int | `60` | 同步间隔（分钟）。最小值为 1 分钟 |

**执行行为：**
- API 启动后延迟 30 秒执行首次同步（等待服务完全就绪）
- 之后每隔 `IntervalMinutes` 分钟执行一次
- 自动同步会遍历**所有** `Enabled = true` 的 DataSource Connector（FileSystem + BlobStorage），逐一执行
- 也可通过 `POST /api/datasources/sync` 随时手动触发

**两个开关相互独立：**

| AutoSync.Enabled | FileSystem.Enabled | 行为 |
|---|---|---|
| `false` | 任意 | 后台服务不启动，不自动同步 |
| `true` | `false` | 后台服务运行，但 FileSystem 不参与同步 |
| `true` | `true` | 后台服务运行，FileSystem 参与每次同步 |

### 在 Azure Container Apps 上开启 Blob 同步

ASP.NET Core 支持通过环境变量覆盖任意 `appsettings.json` 配置项，嵌套层级使用 `__`（双下划线）分隔。

**方式一：Azure Portal**

进入 **Container Apps → `vedaaide-dev-api` → Settings → Environment variables**，添加以下环境变量：

| 名称 | 值 |
|------|----|
| `Veda__DataSources__BlobStorage__Enabled` | `true` |
| `Veda__DataSources__BlobStorage__AccountUrl` | `https://<存储账户名>.blob.core.windows.net` |
| `Veda__DataSources__BlobStorage__ContainerName` | `my-docs` |
| `Veda__DataSources__AutoSync__Enabled` | `true` |
| `Veda__DataSources__AutoSync__IntervalMinutes` | `60` |

**方式二：Azure CLI**

```bash
az containerapp update \
  --name vedaaide-dev-api \
  --resource-group <resource-group> \
  --set-env-vars \
    "Veda__DataSources__BlobStorage__Enabled=true" \
    "Veda__DataSources__BlobStorage__AccountUrl=https://<存储账户名>.blob.core.windows.net" \
    "Veda__DataSources__BlobStorage__ContainerName=my-docs" \
    "Veda__DataSources__AutoSync__Enabled=true" \
    "Veda__DataSources__AutoSync__IntervalMinutes=60"
```

**云端认证方式（推荐：Managed Identity 无密钥）**

填写 `AccountUrl`，`ConnectionString` 留空，然后为应用的 Managed Identity 授予存储账户访问权限：

```bash
# 获取存储账户资源 ID
STORAGE_ID=$(az storage account show \
  --name <存储账户名> \
  --resource-group <resource-group> \
  --query id -o tsv)

# 为 Managed Identity 授予 Storage Blob Data Reader 角色
az role assignment create \
  --assignee <managed-identity-principal-id> \
  --role "Storage Blob Data Reader" \
  --scope "$STORAGE_ID"
```

如需使用连接字符串，建议存为 Container App Secret，避免明文暴露：

```bash
# 先将连接字符串存为 Secret
az containerapp secret set \
  --name vedaaide-dev-api \
  --resource-group <resource-group> \
  --secrets "blobconn=<连接字符串>"

# 在环境变量中引用 Secret
az containerapp update \
  --name vedaaide-dev-api \
  --resource-group <resource-group> \
  --set-env-vars "Veda__DataSources__BlobStorage__ConnectionString=secretref:blobconn"
```

**验证同步是否正常运行**

```bash
az containerapp logs show \
  --name vedaaide-dev-api \
  --resource-group <resource-group> \
  --follow
```

启动后预期看到以下日志：
```
DataSourceSyncBackgroundService: starting, interval = 60 min
DataSourceSyncBackgroundService: running scheduled sync
BlobStorageConnector: sync complete — 5 ingested, 0 unchanged, 12 chunks, 0 errors
```

---

## 6. 存储后端切换（二期新增）

配置节：`Veda:StorageProvider` / `Veda:CosmosDb`

```json
{
  "Veda": {
    "StorageProvider": "Sqlite",
    "CosmosDb": {
      "Endpoint": "https://YOUR_ACCOUNT.documents.azure.com:443/",
      "AccountKey": "",
      "DatabaseName": "VedaAide",
      "ChunksContainerName": "VectorChunks",
      "CacheContainerName": "SemanticCache",
      "EmbeddingDimensions": 1024
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `StorageProvider` | string | `Sqlite` | 向量存储后端。`Sqlite`（本地开发）或 `CosmosDb`（云端部署） |
| `CosmosDb:Endpoint` | string | `""` | CosmosDB 账户端点（`StorageProvider=CosmosDb` 时必填） |
| `CosmosDb:AccountKey` | string | `""` | 账户主键，**留空则使用 Managed Identity**（推荐云端使用） |
| `CosmosDb:DatabaseName` | string | `VedaAide` | CosmosDB 数据库名 |
| `CosmosDb:ChunksContainerName` | string | `VectorChunks` | 向量块容器名（含 DiskANN 索引） |
| `CosmosDb:CacheContainerName` | string | `SemanticCache` | 语义缓存容器名 |
| `CosmosDb:EmbeddingDimensions` | int | `1024` | Embedding 向量维度，须与模型一致（bge-m3=1024，text-embedding-3-small=1536） |

> SQLite 元数据库（PromptTemplate / SyncState / Eval）始终使用 SQLite，不受此配置影响。

---

## 7. AI 提供商切换（二期新增）

配置节：`Veda:EmbeddingProvider` / `Veda:LlmProvider` / `Veda:AzureOpenAI`

```json
{
  "Veda": {
    "EmbeddingProvider": "Ollama",
    "LlmProvider": "Ollama",
    "AzureOpenAI": {
      "Endpoint": "https://YOUR_ACCOUNT.openai.azure.com/",
      "ApiKey": "",
      "EmbeddingDeployment": "text-embedding-3-small",
      "ChatDeployment": "gpt-4o-mini"
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EmbeddingProvider` | string | `Ollama` | `Ollama` 或 `AzureOpenAI` |
| `LlmProvider` | string | `Ollama` | `Ollama` 或 `AzureOpenAI` |
| `AzureOpenAI:Endpoint` | string | `""` | Azure OpenAI 资源端点 |
| `AzureOpenAI:ApiKey` | string | `""` | API Key，**留空则使用 Managed Identity** |
| `AzureOpenAI:EmbeddingDeployment` | string | `text-embedding-3-small` | Embedding 部署名称 |
| `AzureOpenAI:ChatDeployment` | string | `gpt-4o-mini` | Chat 完成部署名称 |

> Managed Identity 认证：在 Azure Container Apps 中为 App 分配 User Assigned Identity，并授予 `Cognitive Services OpenAI User` 角色，然后将 `AzureOpenAI:ApiKey` 留空即可。

---

## 8. DeepSeek 高级推理（二期新增）

配置节：`Veda:DeepSeek`

```json
{
  "Veda": {
    "DeepSeek": {
      "BaseUrl": "https://api.deepseek.com/v1",
      "ApiKey": "",
      "ChatModel": "deepseek-chat"
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `DeepSeek:BaseUrl` | string | `https://api.deepseek.com/v1` | DeepSeek API 端点（支持 OpenAI 兼容格式） |
| `DeepSeek:ApiKey` | string | `""` | DeepSeek API Key。**留空则 `mode=advanced` 自动降级回默认 LLM** |
| `DeepSeek:ChatModel` | string | `deepseek-chat` | 模型名称（如 `deepseek-reasoner`） |

查询时通过 `mode` 参数路由：
- `"mode": "Simple"` → 使用 LlmProvider（默认 Ollama/AzureOpenAI）
- `"mode": "Advanced"` → 使用 DeepSeek（ApiKey 未配置时降级到 Simple）

---

## 9. API 安全（二期新增）

配置节：`Veda:Security`

```json
{
  "Veda": {
    "Security": {
      "ApiKey": "",
      "AdminApiKey": "",
      "AllowedOrigins": "*"
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Security:ApiKey` | string | `""` | 接口通用 API Key。请求须携带 `X-Api-Key: xxx` 请求头。**留空则关闭认证（仅限开发）** |
| `Security:AdminApiKey` | string | `""` | 管理接口专用 Key（`/api/admin/*`）。**留空则关闭管理接口认证** |
| `Security:AllowedOrigins` | string | `*` | CORS 允许来源，可填逗号分隔的 URL 列表（如 `https://your-site.com`）。`*` 表示允许所有来源 |

豁免路径（无需 API Key）：`/swagger`、`/graphql`、`/mcp`、`/health`。

---

## 10. 语义缓存（二期新增）

配置节：`Veda:SemanticCache`

```json
{
  "Veda": {
    "SemanticCache": {
      "Enabled": false,
      "SimilarityThreshold": 0.95,
      "TtlSeconds": 3600
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `SemanticCache:Enabled` | bool | `false` | 是否启用语义缓存。启用后对语义相似的重复问题直接返回缓存答案，跳过向量检索和 LLM 调用 |
| `SemanticCache:SimilarityThreshold` | float | `0.95` | 问题 Embedding 余弦相似度阈值。高于此值视为"相同语义"命中缓存 |
| `SemanticCache:TtlSeconds` | int | `3600` | 缓存条目存活时间（秒）。超时后自动失效，下次查询重新走 RAG 管道 |

清空缓存：`DELETE /api/admin/cache`（需 Admin API Key）。

---

## 11. 混合检索（三期新增）

配置节：`Veda:Rag`（在现有 RAG 配置节中扩展）

```json
{
  "Veda": {
    "Rag": {
      "HybridRetrievalEnabled": true,
      "VectorWeight": 0.7,
      "KeywordWeight": 0.3,
      "FusionStrategy": "Rrf"
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `HybridRetrievalEnabled` | bool | `true` | 是否启用向量+关键词双通道混合检索。`false` 时仅使用向量检索 |
| `VectorWeight` | float | `0.7` | 向量通道在 RRF 融合时的权重 |
| `KeywordWeight` | float | `0.3` | 关键词通道在 RRF 融合时的权重 |
| `FusionStrategy` | string | `Rrf` | 融合策略。`Rrf`（Reciprocal Rank Fusion，推荐）或 `WeightedSum` |

**适用场景：**
- 精确词汇查询（人名、日期、文件编号）：关键词通道提升召回
- 语义相近但表述不同的查询：向量通道保持优势
- 双通道 RRF 融合对两类查询均有稳定表现

---

## 12. 富格式文档摄取（三期新增）

配置节：`Veda:DocumentIntelligence` / `Veda:Vision`

```json
{
  "Veda": {
    "DocumentIntelligence": {
      "Endpoint": "",
      "ApiKey": ""
    },
    "Vision": {
      "Enabled": false,
      "OllamaModel": "",
      "ChatDeployment": "gpt-4o-mini",
      "TimeoutSeconds": 300
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `DocumentIntelligence:Endpoint` | string | `""` | Azure AI Document Intelligence 端点（Bicep 自动注入）。留空则跳过 OCR 摄取流程 |
| `DocumentIntelligence:ApiKey` | string | `""` | Document Intelligence API Key。**留空则使用 Managed Identity**（云端推荐） |
| `Vision:Enabled` | bool | `false` | 是否启用 Vision 模型处理 `RichMedia` 类型文件 |
| `Vision:OllamaModel` | string | `""` | Ollama 视觉模型名（如 `qwen3-vl:8b`）。**非空则优先使用 Ollama**；留空则自动尝试 AzureOpenAI |
| `Vision:ChatDeployment` | string | `"gpt-4o-mini"` | AzureOpenAI 视觉部署名。`OllamaModel` 为空且 `AzureOpenAI:Endpoint` 已配置时生效 |
| `Vision:TimeoutSeconds` | int | `300` | Ollama 视觉推理 HTTP 超时秒数（视觉模型负载较重时需适当延长） |

**文件上传端点：**

```
POST /api/documents/upload  (multipart/form-data)
  file           binary    图片（JPEG/PNG/WebP/TIFF/BMP）或 PDF，最大 20 MB
  documentType   string?   可选，默认由文件名推断（BillInvoice/RichMedia/...）
  documentName   string?   可选，默认使用原始文件名
```

**DocumentType 路由策略：**

| DocumentType | 提取器 | 说明 |
|---|---|---|
| `BillInvoice` | DocumentIntelligenceFileExtractor | `prebuilt-invoice` 结构化发票提取 |
| 其他（非 RichMedia） | DocumentIntelligenceFileExtractor | `prebuilt-read` 通用 OCR |
| `RichMedia` | VisionModelFileExtractor | GPT-4o-mini Vision，适用于几何图形/手写批注 |

---

## 13. 语义增强层（三期新增）

配置节：`Veda:Semantics`

```json
{
  "Veda": {
    "Semantics": {
      "VocabularyFilePath": ""
    }
  }
}
```

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Semantics:VocabularyFilePath` | string | `""` | 个人词库 JSON 配置文件路径（绝对路径或相对于 API 工作目录）。**留空则使用 `NoOpSemanticEnhancer`（不做任何语义增强）** |

**个人词库文件格式（JSON）：**

```json
{
  "vocabulary": [
    { "term": "bg", "synonyms": ["背景资料", "背景文档", "context"] },
    { "term": "gy", "synonyms": ["工作总结", "工作汇报"] },
    { "term": "Q4", "synonyms": ["第四季度", "四季度"] }
  ],
  "tags": [
    { "pattern": "血压|血糖|体检", "labels": ["健康", "医疗档案"] },
    { "pattern": "预算|支出|收入", "labels": ["财务", "账单"] }
  ]
}
```

**运行机制：**
- 查询时：`QueryService` 在生成 Embedding 前调用 `ISemanticEnhancer.ExpandQueryAsync`，将缩写扩为同义词集合
- 摄取时：`DocumentIngestService` 在 chunk 入库时调用 `GetAliasTagsAsync`，将别名作为 `metadata.aliasTags` 附加存储
- 词库文件由用户独立提供，不影响核心代码

---

## 14. User Secrets（开发环境敏感参数）

`UserSecretsId`：`78511e53-5061-4af3-a532-980931a060a8`（`Veda.Api.csproj` 中配置）

**初始化**（已完成，无需重复执行）：

```bash
cd src/Veda.Api
dotnet user-secrets init
```

**存储敏感配置**（不提交到 Git）：

```bash
# Blob Storage 连接字符串
dotnet user-secrets set "Veda:DataSources:BlobStorage:ConnectionString" "DefaultEndpointsProtocol=..."

# Azure OpenAI + CosmosDB（本地连云端调试时使用）
dotnet user-secrets set "Veda:StorageProvider" "CosmosDb"
dotnet user-secrets set "Veda:EmbeddingProvider" "AzureOpenAI"
dotnet user-secrets set "Veda:LlmProvider" "AzureOpenAI"
dotnet user-secrets set "Veda:AzureOpenAI:Endpoint" "https://YOUR_ACCOUNT.openai.azure.com/"
dotnet user-secrets set "Veda:AzureOpenAI:ApiKey" "<key>"          # 使用 az login 时可省略
dotnet user-secrets set "Veda:AzureOpenAI:EmbeddingDeployment" "text-embedding-3-small"
dotnet user-secrets set "Veda:AzureOpenAI:ChatDeployment" "gpt-4o-mini"
dotnet user-secrets set "Veda:CosmosDb:Endpoint" "https://YOUR_ACCOUNT.documents.azure.com:443/"
dotnet user-secrets set "Veda:CosmosDb:AccountKey" "<key>"         # 使用 az login 时可省略
dotnet user-secrets set "Veda:CosmosDb:EmbeddingDimensions" "1536"
dotnet user-secrets set "Veda:EmbeddingModel" "text-embedding-3-small"

# 查看 / 删除
dotnet user-secrets list
dotnet user-secrets remove "Veda:AzureOpenAI:ApiKey"
```

**User Secrets 文件位置**（自动管理，勿手动编辑）：
- Windows：`%APPDATA%\Microsoft\UserSecrets\78511e53-5061-4af3-a532-980931a060a8\secrets.json`
- Linux/macOS：`~/.microsoft/usersecrets/78511e53-5061-4af3-a532-980931a060a8/secrets.json`

> **注意：** User Secrets 仅在 `ASPNETCORE_ENVIRONMENT=Development` 时自动加载（.NET 默认行为）。
> 本项目通过在 `Program.cs` 中显式调用 `AddUserSecrets<Program>(optional: true)` 使其在所有环境生效，但生产环境建议改用环境变量或 Azure Key Vault。

---

## 15. 环境变量覆盖

所有 `appsettings.json` 配置项均可通过环境变量覆盖（ASP.NET Core 标准行为）。
嵌套配置使用 `__`（双下划线）分隔层级：

```bash
# 示例：覆盖 BlobStorage 的连接字符串
export Veda__DataSources__BlobStorage__ConnectionString="xxxxx"
export Veda__DataSources__BlobStorage__Enabled="true"

# 覆盖 Embedding 模型
export Veda__EmbeddingModel="nomic-embed-text"

# Docker Compose / Kubernetes 中同理
environment:
  - Veda__DataSources__BlobStorage__ConnectionString=xxxxx
  - Veda__DataSources__AutoSync__Enabled=true
```

---

## 16. 配置优先级

从低到高（高优先级覆盖低优先级）：

```
appsettings.json
  ↓ 覆盖
appsettings.{Environment}.json（如 appsettings.Development.json）
  ↓ 覆盖
环境变量（Veda__xxx__yyy 格式）
  ↓ 覆盖
User Secrets（dotnet user-secrets set ...）← 最高优先级
```

> **实际用途：** 在 `appsettings.json` 中设置默认值（安全的，可提交到 Git），在 User Secrets 或环境变量中覆盖敏感值（如 ConnectionString、AccountUrl）。
> 这样即使 `appsettings.json` 泄露也不包含任何凭据。
