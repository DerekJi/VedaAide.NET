# VedaAide.NET 配置说明

本文档系统说明 `appsettings.json`、环境变量及 User Secrets 的所有可配置项。

---

## 目录

1. [核心设置](#1-核心设置)
2. [RAG 参数](#2-rag-参数)
3. [数据源：文件系统](#3-数据源文件系统)
4. [数据源：Azure Blob Storage](#4-数据源azure-blob-storage)
5. [数据源：自动同步](#5-数据源自动同步)
6. [User Secrets（开发环境敏感参数）](#6-user-secrets开发环境敏感参数)
7. [环境变量覆盖](#7-环境变量覆盖)
8. [配置优先级](#8-配置优先级)

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

---

## 6. User Secrets（开发环境敏感参数）

`UserSecretsId`：`78511e53-5061-4af3-a532-980931a060a8`（`Veda.Api.csproj` 中配置）

**初始化**（已完成，无需重复执行）：

```bash
cd src/Veda.Api
dotnet user-secrets init
```

**存储敏感配置**（不提交到 Git）：

```bash
# 存储 Blob Storage 连接字符串（代替在 appsettings 中明文填写）
dotnet user-secrets set "Veda:DataSources:BlobStorage:ConnectionString" "DefaultEndpointsProtocol=..."

# 启用 BlobStorage（也可通过 User Secrets 覆盖）
dotnet user-secrets set "Veda:DataSources:BlobStorage:Enabled" "true"

# 查看所有 User Secrets
dotnet user-secrets list

# 删除某个 Secret
dotnet user-secrets remove "Veda:DataSources:BlobStorage:ConnectionString"
```

**User Secrets 文件位置**（自动管理，勿手动编辑）：
- Windows：`%APPDATA%\Microsoft\UserSecrets\78511e53-5061-4af3-a532-980931a060a8\secrets.json`
- Linux/macOS：`~/.microsoft/usersecrets/78511e53-5061-4af3-a532-980931a060a8/secrets.json`

> **注意：** User Secrets 仅在 `ASPNETCORE_ENVIRONMENT=Development` 时自动加载（.NET 默认行为）。
> 本项目通过在 `Program.cs` 中显式调用 `AddUserSecrets<Program>(optional: true)` 使其在所有环境生效，但生产环境建议改用环境变量或 Azure Key Vault。

---

## 7. 环境变量覆盖

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

## 8. 配置优先级

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
