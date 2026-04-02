# Integration Testing Guide

## Overview

This document describes the design, execution, and validation approach for VedaAide integration tests — covering the complete Ingest → vector retrieval → LLM Q&A pipeline without relying on any external services.

---

## Test Layers

| Layer | Storage | AI Services | Representative File |
|-------|---------|-------------|-------------------|
| Unit tests | `Mock<IVectorStore>` | All mocked | `QueryServiceTests.cs` |
| Storage integration tests | SQLite in-memory | None | `SqliteVectorStoreIntegrationTests.cs` |
| **Pipeline integration tests** | **SQLite in-memory** | **Fake stubs** | **`Integration/IngestQueryIntegrationTests.cs`** |
| Manual smoke tests | Disk `veda.db` | Ollama / Azure | `test-data/questions.md` |

---

## Pipeline Integration Test Design

### Core Approach

Replace the disk-based `veda.db` with **SQLite `DataSource=:memory:`** and replace Ollama / Azure OpenAI with fake services:

- No disk files written
- Each test case has an isolated DB instance (automatically destroyed by `[TearDown]`)
- No need to start Ollama or configure Azure credentials
- Runs with zero configuration in CI/CD environments

### Fake Services

**`FakeEmbeddingService`**

Generates deterministic unit vectors (384-dimensional) based on SHA-256 hashes:

- Same text → same vector (cosine = 1.0) — ensures queries precisely hit ingested documents
- Different text → different vectors — vector retrieval discrimination works correctly
- No external processes or network calls required

**`FakeChatService`**

Returns the `userMessage` (including retrieved context) as-is, allowing assertions to directly verify which chunks were retrieved without caring about LLM generation quality.

### Test Architecture

```
SqliteConnection("DataSource=:memory:")
    └── VedaDbContext (EF Core)
         └── SqliteVectorStore ← real implementation
              ├── DocumentIngestService ← real implementation
              │    ├── TextDocumentProcessor  (real: chunking)
              │    ├── FakeEmbeddingService   (replaces Ollama)
              │    └── file extractors        (stub: not called in tests)
              └── QueryService ← real implementation
                   ├── FakeEmbeddingService   (replaces Ollama)
                   └── FakeChatService        (replaces LLM)
```

---

## How to Run

### Integration tests only

```bash
dotnet test tests/Veda.Services.Tests --filter "Category=Integration"
```

### All tests (including integration)

```bash
dotnet test tests/Veda.Services.Tests
```

### Skip integration tests (unit tests only)

```bash
dotnet test tests/Veda.Services.Tests --filter "Category!=Integration"
```

---

## Local Development: Manual API-Level Smoke Testing

Use the following workflow when you need to validate end-to-end with a real Ollama instance and real PDF files.

### DevBypass Authentication Mechanism

All API endpoints require an Entra ID JWT by default. In Development mode, `DevBypassAuthHandler` can bypass authentication so all requests pass with the fixed identity `oid=dev-user`.

**Activation conditions (both must be true):**

1. `ASPNETCORE_ENVIRONMENT=Development`
2. `appsettings.Development.json` contains `"Veda": { "DevMode": { "NoAuth": true } }`

> **Warning:** Never enable `NoAuth=true` in production.

**Important: How NoAuth affects frontend login**

`NoAuth=true` only affects the backend API — it is completely independent of the frontend. The two layers operate separately:

- **Frontend**: Routes remain protected by `MsalGuard`; users still need to sign in via Entra ID, and `MsalInterceptor` still attaches a real Bearer Token to every request.
- **Backend**: `DevBypassAuthHandler` ignores any token in the request entirely and forces all requests to be identified as `oid=dev-user`.

In other words, even if the frontend user has signed in with a real account, that identity is never passed to the backend — all requests are processed as `dev-user`.

| Scenario | Frontend Token | Backend Identity |
|----------|---------------|-----------------|
| `NoAuth=true` | Present (MSAL-attached) | `dev-user` (token ignored) |
| `NoAuth=true` | Absent (curl/Postman) | `dev-user` (token ignored) |
| `NoAuth=false` (production) | Present | Real Entra user |
| `NoAuth=false` (production) | Absent | 401 Unauthorized |

The primary purpose of `NoAuth` is to allow tools like `curl` and Postman to access the API directly without a token, making local debugging easier.

### 1. Start the API

```bash
cd src/Veda.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

### 2. Ingest a PDF

```bash
curl -X POST 'http://localhost:5126/api/documents/upload?documentType=Certificate&documentName=ICAS-Maths' \
  -F 'file=@test-data/ICAS-Year5.Maths.pdf;type=application/pdf'
```

### 3. Query

```bash
curl -X POST http://localhost:5126/api/query \
  -H "Content-Type: application/json" \
  -d '{"question": "How is Marco'\''s maths result?", "topK": 5, "minSimilarity": 0.3}'
```

### 4. Clean up test data (optional)

```bash
curl -X DELETE -H "X-Confirm: yes" http://localhost:5126/api/admin/data
```

---

## DedupThreshold for Certificate Document Type

`ChunkingOptions.Certificate` sets `DedupThreshold = 1.0f`, which effectively disables semantic deduplication.

**Reason:** ICAS certificates of the same category (English / Maths / Science) use identical templates, causing embedding vector cosine similarity to exceed 0.97. The original threshold of 0.70 caused certificates from different subjects to be incorrectly treated as near-duplicates and discarded.

`1.0f` means only chunks with a cosine similarity of exactly 1.0 are considered near-duplicates (which is practically impossible), while exact-content deduplication is still enforced via `ContentHash` (SHA-256).
