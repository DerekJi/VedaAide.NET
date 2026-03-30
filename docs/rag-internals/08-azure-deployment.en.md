> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[08-azure-deployment.cn.md](08-azure-deployment.cn.md)

# 08 — Azure Deployment Architecture

> VedaAide's infrastructure layout on Azure, and how local development maps to cloud resources.

---

## 1. Azure Production Architecture

```plantuml
@startuml azure-architecture
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam componentStyle rectangle

title VedaAide — Azure Production Deployment Architecture

actor "User / External LLM\n(via MCP)" as User

cloud "Azure (australiaeast)" as Azure {

  package "Azure Container Apps Environment\nvedaaide-dev (Consumption plan)" as CAE #E8F4FD {
    [vedaaide-dev-api\nContainer App\n(Veda.Api)\nPort 8080 / HTTPS] as API

    note right of API
      Image: ghcr.io/<org>/vedaaide-api:latest
      Replicas: 1-10 (auto-scale by traffic)
      CPU: 0.5 core / Memory: 1Gi
      Ingress: external (HTTPS)
      Probe: GET /health
    end note
  }

  package "Azure AI Services" as AI #FFF3CD {
    [Azure OpenAI\ngpt-4o-mini (Chat)\ntext-embedding-3-small (Embedding)\nDeploy: australiaeast] as AOI

    [Azure AI Document Intelligence\nprebuilt-invoice\nprebuilt-read\n(PDF / Image OCR)] as DI
  }

  package "Storage" as Storage #E8F8E8 {
    database "Azure CosmosDB for NoSQL\nDatabase: VedaAide\nContainer: VectorChunks (DiskANN)\nContainer: SemanticCache\nPartition Key: /documentId" as CDB

    [Azure Blob Storage\nContainer: docs\n(document data source)] as BLOB
  }

  package "Observability" as Obs #F8E8FF {
    [Log Analytics Workspace\nContainer App logs\nStructured JSON logs] as LAW
  }

  package "CI/CD" as CICD #F0F8FF {
    [GitHub Actions\n.github/workflows/\ndocker build → push → deploy] as GHA
    [GitHub Container Registry\nghcr.io\n(container image registry)] as GHCR
  }

  package "Infrastructure as Code" as IaC #FFF8E8 {
    [Bicep Templates\ninfra/main.bicep\n+ modules/container-apps.bicep] as Bicep
  }
}

' Traffic
User     --> API       : HTTPS\nREST / GraphQL / SSE / MCP

' API calls to external services
API --> AOI   : Embedding generation\nChat completion
API --> DI    : File content extraction (PDF/image)
API --> CDB   : Vector read/write (production)
API --> BLOB  : BlobStorageConnector data source sync
API --> LAW   : Structured logging

' CI/CD
GHA --> GHCR  : docker build & push
GHA --> API   : az containerapp update (deploy)
Bicep --> CAE : az deployment group create

note bottom of CDB
  Auth: DefaultAzureCredential
  (Managed Identity / az login)
  AccountKey only for local debugging

  Vector index policy:
  {
    "path": "/embedding",
    "type": "diskann",
    "distanceFunction": "cosine",
    "dimensions": 1536
  }
end note

note bottom of AOI
  Auth: DefaultAzureCredential
  (Managed Identity preferred, no plaintext API key)

  DeepSeek (Advanced mode):
  External API, apiKey auth
  Base URL: https://api.deepseek.com/v1
end note

@enduml
```

---

## 2. Local Dev vs Production Environment

```plantuml
@startuml local-vs-prod
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Local Dev vs Production Environment

rectangle "Local Development\n(appsettings.Development.json)" as Local #E8F8E8 {
  note as LNote
    StorageProvider:   Sqlite
    EmbeddingProvider: Ollama
    LlmProvider:       Ollama

    Vector store:  SQLite file (veda.db)
    Embedding:     Ollama bge-m3 (localhost:11434)
    Chat LLM:      Ollama qwen3:8b (localhost:11434)
    File extract:  DocIntelligence (requires endpoint config)
    Deploy:        dotnet run directly

    Pros: No Azure costs, works offline
    Cons: Full-scan vector search, slower model inference
  end note
}

rectangle "Production\n(appsettings.json + env vars)" as Prod #E8F4FD {
  note as PNote
    StorageProvider:   CosmosDb
    EmbeddingProvider: AzureOpenAI
    LlmProvider:       AzureOpenAI

    Vector store:  CosmosDB DiskANN
    Embedding:     Azure OpenAI text-embedding-3-small
    Chat LLM:      Azure OpenAI gpt-4o-mini / DeepSeek
    File extract:  Azure Document Intelligence
    Deploy:        Azure Container Apps

    Pros: DiskANN approximate search, fast API response
          Managed Identity — no key management needed
    Cons: Metered billing
  end note
}

rectangle "Config Switch Method" as Switch #FFF3CD {
  note as SNote
    Environment variables (highest priority):
    Veda__StorageProvider=CosmosDb
    Veda__EmbeddingProvider=AzureOpenAI
    Veda__LlmProvider=AzureOpenAI
    Veda__CosmosDb__Endpoint=https://...
    Veda__AzureOpenAI__Endpoint=https://...

    User Secrets (local dev key management):
    dotnet user-secrets set "Veda:CosmosDb:AccountKey" "..."

    Priority:
    User Secrets > Env Vars > appsettings.json
    (Program.cs re-registers User Secrets to guarantee highest priority)
  end note
}

Local <--> Switch : for development
Prod  <--> Switch : for deployment

@enduml
```

---

## 3. CI/CD Deployment Flow

```plantuml
@startuml cicd-flow
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title GitHub Actions CI/CD Flow

participant "Developer" as Dev
participant "GitHub\n(main branch)" as GH
participant "GitHub Actions" as GHA
participant "GitHub Container\nRegistry (ghcr.io)" as GHCR
participant "Azure Container Apps" as ACA

Dev -> GH : git push / merge PR to main

GH -> GHA : trigger workflow

GHA -> GHA : dotnet build
GHA -> GHA : dotnet test
GHA -> GHA : docker build -t ghcr.io/<org>/vedaaide-api:latest\n(Dockerfile in src/Veda.Api/)
GHA -> GHCR : docker push

GHA -> ACA : az containerapp update\n--image ghcr.io/<org>/vedaaide-api:latest\n--name vedaaide-dev-api

ACA -> ACA : Rolling update\n(old instances keep serving until new instance is healthy)
ACA -> ACA : GET /health probe passes

ACA --> Dev : Deployment complete

note right of GHA
  infra/main.bicep describes full infrastructure
  First-time provisioning:
  az deployment group create \
    --template-file infra/main.bicep \
    --parameters @infra/main.parameters.json

  Subsequent image updates: az containerapp update
end note

@enduml
```

---

## 4. Security Design

| Security Measure | Implementation |
|-----------------|---------------|
| **API Authentication** | `X-Api-Key` request header, validated by `ApiKeyMiddleware`; admin endpoints use `AdminApiKey` |
| **Azure Service Auth** | `DefaultAzureCredential` (Managed Identity / az login); no plaintext API keys in production |
| **HTTPS** | Azure Container Apps enables TLS by default, HTTP → HTTPS redirect |
| **CORS** | `Veda:Security:AllowedOrigins` configures whitelist; default `*` only for development |
| **Rate Limiting** | Fixed window 60 requests/minute (`RateLimiterMiddleware`), prevents abuse |
| **File Upload Limits** | `RequestSizeLimit(20MB)`, only JPEG/PNG/WebP/TIFF/BMP/PDF allowed |
| **Secrets Management** | User Secrets (dev) / Environment vars (prod) / Azure Key Vault (recommended) |
| **Log Safety** | API keys never logged in plaintext; vector data does not contain PII |
| **Privacy by Design** | `UserBehaviors` table only stores chunkId + userId, not raw content |

---

## 5. Health Check Endpoints

| Endpoint | Description | Dependency |
|----------|-------------|------------|
| `GET /health` | Overall health status | All registered health checks |
| `GET /health/ready` | Readiness probe (traffic switchover) | — |
| `CosmosDbHealthCheck` | Validates CosmosDB connectivity | Registered when `StorageProvider=CosmosDb` |
| `AzureOpenAIConfigHealthCheck` | Validates Azure OpenAI configuration | Registered when `EmbeddingProvider=AzureOpenAI` |
