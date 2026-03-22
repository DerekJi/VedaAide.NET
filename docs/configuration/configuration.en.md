# VedaAide.NET Configuration Reference

This document covers all configurable fields in `appsettings.json`, environment variables, and User Secrets.

---

## Table of Contents

1. [Core Settings](#1-core-settings)
2. [RAG Parameters](#2-rag-parameters)
3. [Data Source: File System](#3-data-source-file-system)
4. [Data Source: Azure Blob Storage](#4-data-source-azure-blob-storage)
5. [Data Source: Auto Sync](#5-data-source-auto-sync)
6. [User Secrets (Development Sensitive Config)](#6-user-secrets-development-sensitive-config)
7. [Environment Variable Overrides](#7-environment-variable-overrides)
8. [Configuration Priority](#8-configuration-priority)

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

---

## 6. User Secrets (Development Sensitive Config)

`UserSecretsId`: `78511e53-5061-4af3-a532-980931a060a8` (set in `Veda.Api.csproj`)

**Initialize** (already done, no need to repeat):

```bash
cd src/Veda.Api
dotnet user-secrets init
```

**Store sensitive values** (not committed to Git):

```bash
# Store Blob Storage connection string (instead of putting it in appsettings.json)
dotnet user-secrets set "Veda:DataSources:BlobStorage:ConnectionString" "DefaultEndpointsProtocol=..."

# Enable BlobStorage via User Secrets
dotnet user-secrets set "Veda:DataSources:BlobStorage:Enabled" "true"

# View all stored secrets
dotnet user-secrets list

# Remove a secret
dotnet user-secrets remove "Veda:DataSources:BlobStorage:ConnectionString"
```

**Secrets file location** (managed automatically, do not edit manually):
- Windows: `%APPDATA%\Microsoft\UserSecrets\78511e53-5061-4af3-a532-980931a060a8\secrets.json`
- Linux/macOS: `~/.microsoft/usersecrets/78511e53-5061-4af3-a532-980931a060a8/secrets.json`

> **Note:** User Secrets are automatically loaded only when `ASPNETCORE_ENVIRONMENT=Development` (standard .NET behavior).
> This project explicitly calls `AddUserSecrets<Program>(optional: true)` in `Program.cs` to enable them in all environments. For production environments, prefer environment variables or Azure Key Vault.

---

## 7. Environment Variable Overrides

All `appsettings.json` fields can be overridden via environment variables (standard ASP.NET Core behavior).
Use `__` (double underscore) to separate nested config sections:

```bash
# Override BlobStorage connection string
export Veda__DataSources__BlobStorage__ConnectionString="xxxxx"
export Veda__DataSources__BlobStorage__Enabled="true"

# Override embedding model
export Veda__EmbeddingModel="nomic-embed-text"

# Docker Compose / Kubernetes
environment:
  - Veda__DataSources__BlobStorage__ConnectionString=xxxxx
  - Veda__DataSources__AutoSync__Enabled=true
```

---

## 8. Configuration Priority

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
