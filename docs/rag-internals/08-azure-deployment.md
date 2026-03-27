# 08 — Azure 部署架构

> VedaAide 在 Azure 上的基础设施布局，以及本地开发环境与云端的对应关系。

---

## 1. Azure 生产架构图

```plantuml
@startuml azure-architecture
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam componentStyle rectangle

title VedaAide — Azure 生产部署架构

actor "用户 / 外部 LLM\n(通过 MCP 调用)" as User

cloud "Azure (australiaeast)" as Azure {

  package "Azure Container Apps Environment\nvedaaide-dev (Consumption plan)" as CAE #E8F4FD {
    [vedaaide-dev-api\nContainer App\n(Veda.Api)\nPort 8080 / HTTPS] as API

    note right of API
      镜像：ghcr.io/<org>/vedaaide-api:latest
      Replicas: 1-10（按流量扩缩）
      CPU: 0.5 core / Memory: 1Gi
      Ingress: 外部可访问 (HTTPS)
      Probe: GET /health
    end note
  }

  package "Azure AI Services" as AI #FFF3CD {
    [Azure OpenAI\ngpt-4o-mini (Chat)\ntext-embedding-3-small (Embedding)\nDeploy: australiaeast] as AOI

    [Azure AI Document Intelligence\nprebuilt-invoice\nprebuilt-read\n(PDF / 图片 OCR)] as DI
  }

  package "存储" as Storage #E8F8E8 {
    database "Azure CosmosDB for NoSQL\nDatabase: VedaAide\nContainer: VectorChunks (DiskANN)\nContainer: SemanticCache\nPartition Key: /documentId" as CDB

    [Azure Blob Storage\nContainer: docs\n(数据源文档仓库)] as BLOB
  }

  package "可观测性" as Obs #F8E8FF {
    [Log Analytics Workspace\nContainer App 日志\n结构化 JSON 日志] as LAW
  }

  package "CI/CD" as CICD #F0F8FF {
    [GitHub Actions\n.github/workflows/\ndocker build → push → deploy] as GHA
    [GitHub Container Registry\nghcr.io\n(容器镜像仓库)] as GHCR
  }

  package "基础设施即代码" as IaC #FFF8E8 {
    [Bicep 模板\ninfra/main.bicep\n+ modules/container-apps.bicep] as Bicep
  }
}

' 流量
User     --> API       : HTTPS\nREST / GraphQL / SSE / MCP

' API 调用外部服务
API --> AOI   : Embedding 生成\nChat 完成
API --> DI    : 文件内容提取（PDF/图片）
API --> CDB   : 向量读写（生产）
API --> BLOB  : BlobStorageConnector 数据源同步
API --> LAW   : 结构化日志

' CI/CD
GHA --> GHCR  : docker build & push
GHA --> API   : az containerapp update (deploy)
Bicep --> CAE : az deployment group create

note bottom of CDB
  认证：DefaultAzureCredential
  (Managed Identity / az login)
  AccountKey 仅用于本地调试

  向量索引策略：
  {
    "path": "/embedding",
    "type": "diskann",
    "distanceFunction": "cosine",
    "dimensions": 1536
  }
end note

note bottom of AOI
  认证：DefaultAzureCredential
  (Managed Identity 优先，无需 API Key 明文)

  DeepSeek (Advanced 模式)：
  外部 API，通过 apiKey 认证
  Base URL: https://api.deepseek.com/v1
end note

@enduml
```

---

## 2. 本地开发 vs 生产环境对照

```plantuml
@startuml local-vs-prod
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 本地开发 vs 生产环境

rectangle "本地开发\n(appsettings.Development.json)" as Local #E8F8E8 {
  note as LNote
    StorageProvider:   Sqlite
    EmbeddingProvider: Ollama
    LlmProvider:       Ollama

    向量库：  SQLite 文件 (veda.db)
    Embedding：Ollama bge-m3 (localhost:11434)
    Chat LLM：Ollama qwen3:8b (localhost:11434)
    文件提取：DocIntelligence (需配置 Endpoint)
    部署：    dotnet run 直接运行

    优势：无 Azure 费用，离线可用
    劣势：向量检索全量扫描，模型推理较慢
  end note
}

rectangle "生产环境\n(appsettings.json + 环境变量)" as Prod #E8F4FD {
  note as PNote
    StorageProvider:   CosmosDb
    EmbeddingProvider: AzureOpenAI
    LlmProvider:       AzureOpenAI

    向量库：  CosmosDB DiskANN
    Embedding：Azure OpenAI text-embedding-3-small
    Chat LLM：Azure OpenAI gpt-4o-mini / DeepSeek
    文件提取：Azure Document Intelligence
    部署：    Azure Container Apps

    优势：DiskANN 近似检索，API 响应快
           Managed Identity 无需管理密钥
    劣势：按用量计费
  end note
}

rectangle "配置切换方式" as Switch #FFF3CD {
  note as SNote
    环境变量（优先级最高）：
    Veda__StorageProvider=CosmosDb
    Veda__EmbeddingProvider=AzureOpenAI
    Veda__LlmProvider=AzureOpenAI
    Veda__CosmosDb__Endpoint=https://...
    Veda__AzureOpenAI__Endpoint=https://...

    User Secrets（本地开发密钥管理）：
    dotnet user-secrets set "Veda:CosmosDb:AccountKey" "..."

    优先级：
    User Secrets > 环境变量 > appsettings.json
    (Program.cs 重新注册 User Secrets 确保最高优先级)
  end note
}

Local <--> Switch : 开发时
Prod  <--> Switch : 部署时

@enduml
```

---

## 3. 部署 CI/CD 流程

```plantuml
@startuml cicd-flow
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title GitHub Actions CI/CD 流程

participant "开发者" as Dev
participant "GitHub\n(main branch)" as GH
participant "GitHub Actions" as GHA
participant "GitHub Container\nRegistry (ghcr.io)" as GHCR
participant "Azure Container Apps" as ACA

Dev -> GH : git push / merge PR to main

GH -> GHA : 触发 workflow

GHA -> GHA : dotnet build
GHA -> GHA : dotnet test
GHA -> GHA : docker build -t ghcr.io/<org>/vedaaide-api:latest\n(Dockerfile in src/Veda.Api/)
GHA -> GHCR : docker push

GHA -> ACA : az containerapp update\n--image ghcr.io/<org>/vedaaide-api:latest\n--name vedaaide-dev-api

ACA -> ACA : 滚动更新\n(旧实例继续服务直到新实例健康)
ACA -> ACA : GET /health 探针通过

ACA --> Dev : 部署完成

note right of GHA
  infra/main.bicep 描述完整基础设施
  首次部署：
  az deployment group create \
    --template-file infra/main.bicep \
    --parameters @infra/main.parameters.json

  后续镜像更新：az containerapp update
end note

@enduml
```

---

## 4. 安全设计

| 安全措施 | 实现方式 |
|---------|---------|
| **API 认证** | `X-Api-Key` 请求头，通过 `ApiKeyMiddleware` 验证；管理接口用 `AdminApiKey` | 
| **Azure 服务认证** | `DefaultAzureCredential`（Managed Identity / az login），生产环境无需明文 API Key |
| **HTTPS** | Azure Container Apps 默认开启 TLS，HTTP → HTTPS 重定向 |
| **CORS** | `Veda:Security:AllowedOrigins` 配置白名单，默认 `*` 仅用于开发 |
| **速率限制** | 固定窗口 60 请求/分钟（`RateLimiterMiddleware`），防止滥用 |
| **文件上传限制** | `RequestSizeLimit(20MB)`，仅允许 JPEG/PNG/WebP/TIFF/BMP/PDF |
| **Secrets 管理** | User Secrets (开发) / 环境变量 (生产) / Azure Key Vault (推荐) |
| **日志安全** | 不记录 API Key 明文；向量数据不包含 PII |
| **隐私设计** | `UserBehaviors` 表只存 chunkId + userId，不记录原始内容 |

---

## 5. 健康检查端点

| 端点 | 说明 | 依赖 |
|------|------|------|
| `GET /health` | 整体健康状态 | 所有注册的 Health Check |
| `GET /health/ready` | 就绪探针（流量切入） | - |
| `CosmosDbHealthCheck` | 验证 CosmosDB 连接 | `StorageProvider=CosmosDb` 时注册 |
| `AzureOpenAIConfigHealthCheck` | 验证 Azure OpenAI 配置 | `EmbeddingProvider=AzureOpenAI` 时注册 |
