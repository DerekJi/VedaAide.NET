# VedaAide.NET Configuration Reference

This document covers all configurable fields in `appsettings.json`, environment variables, and User Secrets.

---

## Table of Contents

1. [Core Settings](#1-core-settings)
2. [RAG Parameters](#2-rag-parameters)
3. [Data Source: File System](#3-data-source-file-system)
4. [Data Source: Azure Blob Storage](#4-data-source-azure-blob-storage)
5. [Data Source: Auto Sync](#5-data-source-auto-sync)
6. [Storage Backend](#6-storage-backend)
7. [AI Provider](#7-ai-provider)
8. [DeepSeek Advanced Reasoning](#8-deepseek-advanced-reasoning)
9. [API Security](#9-api-security)
10. [Semantic Cache](#10-semantic-cache)
11. [User Secrets (Development Sensitive Config)](#11-user-secrets-development-sensitive-config)
12. [Environment Variable Overrides](#12-environment-variable-overrides)
13. [Configuration Priority](#13-configuration-priority)

---

## 1. Core Settings

Section: `Veda`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `OllamaEndpoint` | string | `http://localhost:11434` | Ollama service URL. Usually no change needed for local deployments |
| `EmbeddingModel` | string | `bge-m3` | Ollama embedding model name. `bge-m3` (1024-dim, multilingual) is recommended for Chinese content; `nomic-embed-text` (768-dim) is a lighter option for English-only. **Switching models requires clearing the vector store and re-ingesting all documents (dimensions are incompatible).** |
| `ChatModel` | string | `qwen3:8b` | Ollama chat model name. Can be swapped for any model already downloaded via Ollama |
| `DbPath` | string | `veda.db` | SQLite database file path, relative to the API working directory. In Docker deployments, mount this to a host Volume |

---

## 2. RAG Parameters

Section: `Veda:Rag`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `SimilarityDedupThreshold` | float | `0.95` | Cosine similarity threshold for semantic deduplication during ingestion. New chunks with similarity ≥ this value to an existing chunk are considered semantic duplicates and skipped. `0.95` is a conservative default; highly standardized documents (e.g., legal texts) can use `0.90` |
| `HallucinationSimilarityThreshold` | float | `0.3` | First-layer hallucination detection threshold. If the LLM answer's embedding is less than this similar to the top vector store result, the response is flagged as a potential hallucination (`IsHallucination = true`). The answer is still returned — only flagged, not blocked |
| `EnableSelfCheckGuard` | bool | `false` | Enables second-layer hallucination detection (LLM self-check). When enabled, each response triggers an extra LLM call for fact-checking. High accuracy but costly — only recommended for high-compliance scenarios |

---

## 3. Data Source: File System

Section: `Veda:DataSources:FileSystem`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Enabled` | bool | `false` | Whether to enable the local file system data source |
| `Path` | string | `""` | Absolute path to the directory to monitor. In Docker, use the container-side path (paired with a Volume mount) |
| `Extensions` | string[] | `[".txt", ".md"]` | Allowed file extensions whitelist. Files with other extensions are skipped |

**Sync behavior:** Only files whose SHA-256 content hash has changed are re-ingested on each sync cycle. Unchanged files are automatically skipped.

---

## 4. Data Source: Azure Blob Storage

Section: `Veda:DataSources:BlobStorage`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Enabled` | bool | `false` | Whether to enable the Azure Blob Storage data source |
| `ConnectionString` | string | `""` | Blob Storage connection string (includes AccountName + Key). **Pick one** of `ConnectionString` or `AccountUrl` — `ConnectionString` takes priority if both are set |
| `AccountUrl` | string | `""` | Blob Storage account URL, e.g. `https://<account>.blob.core.windows.net`. Used for keyless authentication via Managed Identity or `az login` (`DefaultAzureCredential`). Ignored when `ConnectionString` is non-empty |
| `ContainerName` | string | `""` | Name of the container to sync. **Required** |
| `Prefix` | string | `""` | Blob path prefix filter. Azure uses `/`-delimited paths to simulate directories, so `docs/` means "only blobs under the `docs/` virtual directory". Leave empty to sync the entire container. Filtering is performed server-side for efficiency |
| `Extensions` | string[] | `[".txt", ".md"]` | Allowed file extensions whitelist |

**Authentication examples:**

```json
// Option A: Connection string (with key)
"ConnectionString": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=xxx;EndpointSuffix=core.windows.net"

// Option B: Managed Identity / az login (keyless)
"AccountUrl": "https://myaccount.blob.core.windows.net"
// Leave ConnectionString empty
```

**Sync behavior:** Blobs are compared by SHA-256 content hash. Only new or changed blobs are re-ingested on each sync cycle.

---

## 5. Data Source: Auto Sync

Section: `Veda:DataSources:AutoSync`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Enabled` | bool | `false` | Whether to enable the background auto-sync service (`DataSourceSyncBackgroundService`) |
| `IntervalMinutes` | int | `60` | Sync interval in minutes. Minimum value is 1 minute |

**Execution behavior:**
- First sync runs 30 seconds after API startup (waits for full initialization)
- Subsequent syncs run every `IntervalMinutes`
- Each cycle iterates over **all** `Enabled = true` connectors (FileSystem + BlobStorage)
- Can also be triggered manually at any time via `POST /api/datasources/sync`

**The two switches are independent:**

| AutoSync.Enabled | FileSystem.Enabled | Behavior |
|---|---|---|
| `false` | any | Background service does not start, no automatic sync |
| `true` | `false` | Background service runs, but FileSystem is excluded from each sync |
| `true` | `true` | Background service runs, FileSystem participates in every sync cycle |

### Enabling Blob Sync on Azure Container Apps

All `appsettings.json` values can be overridden via environment variables (ASP.NET Core standard). Nested sections use `__` (double underscore) as separator.

**Method 1: Azure Portal**

Navigate to **Container Apps → `vedaaide-dev-api` → Settings → Environment variables** and add:

| Name | Value |
|------|-------|
| `Veda__DataSources__BlobStorage__Enabled` | `true` |
| `Veda__DataSources__BlobStorage__AccountUrl` | `https://<storageaccount>.blob.core.windows.net` |
| `Veda__DataSources__BlobStorage__ContainerName` | `my-docs` |
| `Veda__DataSources__AutoSync__Enabled` | `true` |
| `Veda__DataSources__AutoSync__IntervalMinutes` | `60` |

**Method 2: Azure CLI**

```bash
az containerapp update \
  --name vedaaide-dev-api \
  --resource-group <resource-group> \
  --set-env-vars \
    "Veda__DataSources__BlobStorage__Enabled=true" \
    "Veda__DataSources__BlobStorage__AccountUrl=https://<storageaccount>.blob.core.windows.net" \
    "Veda__DataSources__BlobStorage__ContainerName=my-docs" \
    "Veda__DataSources__AutoSync__Enabled=true" \
    "Veda__DataSources__AutoSync__IntervalMinutes=60"
```

**Authentication for cloud deployment (Managed Identity — recommended)**

Set `AccountUrl` (leave `ConnectionString` empty), then grant the app's Managed Identity access to the storage account:

```bash
# Replace with your actual values
STORAGE_ID=$(az storage account show \
  --name <storageaccount> \
  --resource-group <resource-group> \
  --query id -o tsv)

az role assignment create \
  --assignee <managed-identity-principal-id> \
  --role "Storage Blob Data Reader" \
  --scope "$STORAGE_ID"
```

Alternatively use `Connection String` (set as a secret, not plain text):

```bash
# Store the connection string as a Container App secret first
az containerapp secret set \
  --name vedaaide-dev-api \
  --resource-group <resource-group> \
  --secrets "blobconn=<connection-string>"

# Reference the secret in environment variables
az containerapp update \
  --name vedaaide-dev-api \
  --resource-group <resource-group> \
  --set-env-vars "Veda__DataSources__BlobStorage__ConnectionString=secretref:blobconn"
```

**Verifying the sync is running**

```bash
az containerapp logs show \
  --name vedaaide-dev-api \
  --resource-group <resource-group> \
  --follow
```

Expected log output after startup:
```
DataSourceSyncBackgroundService: starting, interval = 60 min
DataSourceSyncBackgroundService: running scheduled sync
BlobStorageConnector: sync complete — 5 ingested, 0 unchanged, 12 chunks, 0 errors
```

---

## 6. Storage Backend

Section: `Veda:StorageProvider` / `Veda:CosmosDb`

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
      "BehaviorsContainerName": "UserBehaviors",
      "TokenUsagesContainerName": "TokenUsages",
      "ChatSessionsContainerName": "ChatSessions",
      "EmbeddingDimensions": 1536
    }
  }
}
```

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `StorageProvider` | string | `Sqlite` | Vector store backend: `Sqlite` (local dev) or `CosmosDb` (cloud) |
| `CosmosDb:Endpoint` | string | `""` | CosmosDB account endpoint. Required when `StorageProvider=CosmosDb` |
| `CosmosDb:AccountKey` | string | `""` | Account primary key. **Leave empty to use Managed Identity** (recommended for cloud) |
| `CosmosDb:DatabaseName` | string | `VedaAide` | CosmosDB database name |
| `CosmosDb:ChunksContainerName` | string | `VectorChunks` | Container for vector chunks (with DiskANN index) |
| `CosmosDb:CacheContainerName` | string | `SemanticCache` | Container for semantic cache entries |
| `CosmosDb:BehaviorsContainerName` | string | `UserBehaviors` | Container for user behavior feedback events (Partition Key = `/userId`) |
| `CosmosDb:TokenUsagesContainerName` | string | `TokenUsages` | Container for AI token usage records (Partition Key = `/userId`) |
| `CosmosDb:ChatSessionsContainerName` | string | `ChatSessions` | Container for multi-session persistence (Partition Key = `/userId`) |
| `CosmosDb:EmbeddingDimensions` | int | `1024` | Embedding vector dimensions — must match the model (bge-m3=1024, text-embedding-3-small=1536) |

> SQLite metadata stores (PromptTemplate / SyncState / Eval) always use SQLite. In CosmosDB mode, vectors, semantic cache, user behaviors, token usage, and chat sessions are all stored in CosmosDB, automatically isolated per user by partition key.

**CosmosDB container requirements:**
- `VectorChunks`: partition key `/documentId`, DiskANN vector index on `/embedding` (Float32, Cosine, dimensions as configured), `/embedding/*` excluded from regular index
- `SemanticCache`: partition key `/id`, TTL enabled (`DefaultTimeToLive = -1`)
- `UserBehaviors`: partition key `/userId`
- `TokenUsages`: partition key `/userId`
- `ChatSessions`: partition key `/userId` (stores both session metadata and messages as separate documents via `type` field)

On startup, `CosmosDbInitializer` automatically creates the containers if they do not exist (requires `Cosmos DB Built-in Data Contributor` role on the account).

---

## 7. AI Provider

Section: `Veda:EmbeddingProvider` / `Veda:LlmProvider` / `Veda:AzureOpenAI`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `EmbeddingProvider` | string | `Ollama` | `Ollama` or `AzureOpenAI` |
| `LlmProvider` | string | `Ollama` | `Ollama` or `AzureOpenAI` |
| `AzureOpenAI:Endpoint` | string | `""` | Azure OpenAI resource endpoint |
| `AzureOpenAI:ApiKey` | string | `""` | API Key. **Leave empty to use Managed Identity** |
| `AzureOpenAI:EmbeddingDeployment` | string | `text-embedding-3-small` | Embedding model deployment name |
| `AzureOpenAI:ChatDeployment` | string | `gpt-4o-mini` | Chat completion deployment name |

> **Managed Identity**: In Azure Container Apps, assign a User Assigned Identity to the app and grant it `Cognitive Services OpenAI User` role on the Azure OpenAI resource. Leave `ApiKey` empty.
>
> **Local dev with `az login`**: `DefaultAzureCredential` automatically picks up `az login` credentials locally — no key needed.

---

## 8. DeepSeek Advanced Reasoning

Section: `Veda:DeepSeek`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `DeepSeek:BaseUrl` | string | `https://api.deepseek.com/v1` | DeepSeek API endpoint (OpenAI-compatible format) |
| `DeepSeek:ApiKey` | string | `""` | DeepSeek API key. **If empty, `mode=Advanced` falls back to the default LLM** |
| `DeepSeek:ChatModel` | string | `deepseek-chat` | Model name (e.g. `deepseek-reasoner`) |

Route via query `mode` field:
- `"mode": "Simple"` → uses `LlmProvider` (Ollama or AzureOpenAI)
- `"mode": "Advanced"` → uses DeepSeek (falls back to Simple if `ApiKey` not configured)

---

## 9. API Security

Section: `Veda:Security`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Security:ApiKey` | string | `""` | General API key. Requests must include `X-Api-Key: xxx` header. **Leave empty to disable auth (dev only)** |
| `Security:AdminApiKey` | string | `""` | Admin-only key for `/api/admin/*`. **Leave empty to disable admin auth** |
| `Security:AllowedOrigins` | string | `*` | CORS allowed origins — comma-separated URLs (e.g. `https://your-site.com`). `*` allows all |

Exempt paths (no API key required): `/swagger`, `/graphql`, `/mcp`, `/health`.

---

## 10. Semantic Cache

Section: `Veda:SemanticCache`

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

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `SemanticCache:Enabled` | bool | `false` | Enable semantic caching. When enabled, semantically similar questions return cached answers, skipping vector search and LLM calls |
| `SemanticCache:SimilarityThreshold` | float | `0.95` | Question embedding cosine similarity threshold. Above this value the question is considered a cache hit |
| `SemanticCache:TtlSeconds` | int | `3600` | Cache entry TTL in seconds. Entries expire automatically |

Clear cache: `DELETE /api/admin/cache` (requires Admin API Key).

---

## 11. User Secrets (Development Sensitive Config)

`UserSecretsId`: `78511e53-5061-4af3-a532-980931a060a8` (set in `Veda.Api.csproj`)

**Initialize** (already done, no need to repeat):

```bash
cd src/Veda.Api
dotnet user-secrets init
```

**Store sensitive values** (not committed to Git):

```bash
# Blob Storage connection string
dotnet user-secrets set "Veda:DataSources:BlobStorage:ConnectionString" "DefaultEndpointsProtocol=..."

# Azure OpenAI (if not using Managed Identity)
dotnet user-secrets set "Veda:StorageProvider" "CosmosDb"
dotnet user-secrets set "Veda:EmbeddingProvider" "AzureOpenAI"
dotnet user-secrets set "Veda:LlmProvider" "AzureOpenAI"
dotnet user-secrets set "Veda:AzureOpenAI:Endpoint" "https://YOUR_ACCOUNT.openai.azure.com/"
dotnet user-secrets set "Veda:AzureOpenAI:ApiKey" "<key>"          # omit to use az login
dotnet user-secrets set "Veda:AzureOpenAI:EmbeddingDeployment" "text-embedding-3-small"
dotnet user-secrets set "Veda:AzureOpenAI:ChatDeployment" "gpt-4o-mini"

# CosmosDB (if not using Managed Identity)
dotnet user-secrets set "Veda:CosmosDb:Endpoint" "https://YOUR_ACCOUNT.documents.azure.com:443/"
dotnet user-secrets set "Veda:CosmosDb:AccountKey" "<key>"         # omit to use az login
dotnet user-secrets set "Veda:CosmosDb:EmbeddingDimensions" "1536"
dotnet user-secrets set "Veda:EmbeddingModel" "text-embedding-3-small"

# View / remove
dotnet user-secrets list
dotnet user-secrets remove "Veda:AzureOpenAI:ApiKey"
```

**Secrets file location** (managed automatically, do not edit manually):
- Windows: `%APPDATA%\Microsoft\UserSecrets\78511e53-5061-4af3-a532-980931a060a8\secrets.json`
- Linux/macOS: `~/.microsoft/usersecrets/78511e53-5061-4af3-a532-980931a060a8/secrets.json`

> **Note:** User Secrets are automatically loaded only when `ASPNETCORE_ENVIRONMENT=Development` (standard .NET behavior).
> This project explicitly calls `AddUserSecrets<Program>(optional: true)` in `Program.cs` to enable them in all environments. For production, prefer environment variables or Azure Key Vault.

---

## 12. Environment Variable Overrides

# Remove a secret
dotnet user-secrets remove "Veda:DataSources:BlobStorage:ConnectionString"
```

**Secrets file location** (managed automatically, do not edit manually):
- Windows: `%APPDATA%\Microsoft\UserSecrets\78511e53-5061-4af3-a532-980931a060a8\secrets.json`
- Linux/macOS: `~/.microsoft/usersecrets/78511e53-5061-4af3-a532-980931a060a8/secrets.json`

> **Note:** User Secrets are automatically loaded only when `ASPNETCORE_ENVIRONMENT=Development` (standard .NET behavior).
> This project explicitly calls `AddUserSecrets<Program>(optional: true)` in `Program.cs` to enable them in all environments. For production environments, prefer environment variables or Azure Key Vault.

---

## 12. Environment Variable Overrides

All `appsettings.json` fields can be overridden via environment variables (standard ASP.NET Core behavior).
Use `__` (double underscore) to separate nested config sections:

```bash
export Veda__DataSources__BlobStorage__ConnectionString="xxxxx"
export Veda__StorageProvider="CosmosDb"
export Veda__CosmosDb__Endpoint="https://YOUR_ACCOUNT.documents.azure.com:443/"
export Veda__EmbeddingProvider="AzureOpenAI"
export Veda__AzureOpenAI__Endpoint="https://YOUR_ACCOUNT.openai.azure.com/"

# Docker Compose / Kubernetes
environment:
  - Veda__StorageProvider=CosmosDb
  - Veda__EmbeddingProvider=AzureOpenAI
  - Veda__DataSources__AutoSync__Enabled=true
```

---

## 13. Configuration Priority

From lowest to highest precedence (higher overrides lower):

```
appsettings.json
  ↓ overridden by
appsettings.{Environment}.json  (e.g., appsettings.Development.json)
  ↓ overridden by
Environment variables  (Veda__xxx__yyy format)
  ↓ overridden by
User Secrets  (dotnet user-secrets set ...)  ← highest precedence
```

> **Practical use:** Set safe defaults in `appsettings.json` (committed to Git), and override sensitive values such as `ConnectionString` or `AccountUrl` via User Secrets or environment variables.
> This way, even if `appsettings.json` is leaked, it contains no credentials.
