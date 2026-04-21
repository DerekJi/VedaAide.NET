# VedaAide.NET

> **Production-grade RAG platform** — built from scratch in C# / .NET 10 with Semantic Kernel.  
> Designed for enterprise private deployment: hybrid retrieval, multi-layer hallucination defence, agent orchestration, MCP integration, and a quantitative evaluation harness.

> 中文文档见 [README.cn.md](README.cn.md)

---

## Why I Built This

Most RAG tutorials stop at "embed → store → search → answer." That's not enough for production. This project is my answer to the question: *what does a genuinely production-ready RAG system actually look like?*

Every architectural decision here was made deliberately — and is documented in [Architecture Decision Records](docs/rag-internals/09-adr.en.md).

---

## Architecture at a Glance

```
┌─────────────────────────────────────────────────────────────────┐
│  Entry Points: REST + GraphQL + SSE + MCP HTTP                  │
├─────────────────────────────────────────────────────────────────┤
│  Agent Layer:  ReAct Agent (SK plugin) · OrchestrationService   │
│  Eval Layer:   Faithfulness · Answer Relevancy · Context Recall  │
│  MCP Server:   search_knowledge_base · ingest · list_documents   │
├─────────────────────────────────────────────────────────────────┤
│  Core Services:                                                  │
│  DocumentIngestService  ──► Chunking → Embedding → Dedup → Store│
│  QueryService           ──► HybridRetriever → ContextWindow      │
│                              → LlmRouter → HallucinationGuard    │
│  EmbeddingService  ·  LlmRouter  ·  SemanticCache               │
├─────────────────────────────────────────────────────────────────┤
│  Storage Layer:  CosmosDB (DiskANN) · SQLite-VSS                 │
│                  SemanticCache · UserMemoryStore · SyncStateStore│
└─────────────────────────────────────────────────────────────────┘
```

Eight layered C# projects, strict dependency direction: `Core → Services → Storage → Entry Points`.  
See [full module dependency diagram](docs/rag-internals/06-module-dependencies.en.md).

---

## Key Engineering Decisions

### 1. Hybrid Retrieval with RRF Fusion
Dense vector search (cosine similarity) and sparse keyword search run concurrently.  
Results are merged via **Reciprocal Rank Fusion (RRF, k=60)** — mathematically sound, no tuning required.  
Both `WeightedSum` and `RRF` strategies are supported and configurable.

> *Why not just vector search?* Keyword search significantly outperforms dense retrieval for exact terms, product codes, and proper nouns. Hybrid covers both failure modes.

### 2. Dual-Layer Hallucination Defence
- **Layer 1 — Self-check:** LLM generates answer + confidence flag in a single structured call
- **Layer 2 — Guard verification:** `HallucinationGuardService` sends answer + retrieved context to a second LLM call as an independent fact-checker

Configurable via `Veda:Rag:EnableSelfCheckGuard`. Adds ~300ms but eliminates unsupported claims.

### 3. Semantic Cache (CosmosDB + SQLite)
Before calling the embedding model or LLM, incoming questions are compared against cached embeddings via cosine similarity.  
Cache hit threshold is configurable (`SemanticCacheOptions:SimilarityThreshold`).  
Two implementations: `CosmosDbSemanticCache` (production) and `SqliteSemanticCache` (local/dev).

### 4. LLM Router
`LlmRouterService` selects the appropriate model based on `QueryMode`:
- `Simple` → lightweight model (Ollama local / GPT-4o-mini)
- `Advanced` → DeepSeek R2 (or any OpenAI-compatible endpoint)

Graceful fallback: if the advanced model is not configured, routes to simple automatically.

### 5. Token-Aware Context Window
`ContextWindowBuilder` selects chunks by similarity score and enforces a strict token budget (conservative 3 chars/token estimate to handle mixed Chinese/English content).  
Prevents context pollution from low-relevance chunks exceeding the LLM window.

### 6. ReAct Agent (Semantic Kernel Plugin)
`VedaKernelPlugin` exposes knowledge base retrieval as a `[KernelFunction]`.  
The SK `ChatCompletionAgent` uses this in a **Reason-Act-Observe** loop — the agent decides *when* and *what* to retrieve, rather than retrieval being hardcoded into the query path.

### 7. MCP Server
VedaAide exposes three tools via the **Model Context Protocol (HTTP transport)**:
- `search_knowledge_base` — semantic search against the vector store
- `list_documents` — browse ingested documents
- `ingest_document` — add content at runtime

Plugs into VS Code Copilot, Claude Desktop, or any MCP-compatible AI assistant with a single config line.

### 8. Quantitative RAG Evaluation
Three scorers, each using LLM-as-a-judge:

| Metric | What It Measures |
|--------|-----------------|
| **Faithfulness** | Every claim in the answer is supported by retrieved context |
| **Answer Relevancy** | The answer actually addresses the question asked |
| **Context Recall** | The retrieved chunks contain the information needed to answer |

Scores are stored, queryable via `/api/evaluation`, and support A/B comparison between retrieval strategies.

---

## Tech Stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| Backend | .NET 10, ASP.NET Core | Clean architecture, 8 projects |
| AI Orchestration | Semantic Kernel 1.73 | Plugin-based ReAct agent |
| Vector DB | Azure CosmosDB (DiskANN) / SQLite-VSS | Pluggable via `IVectorStore` |
| LLM / Embedding | Ollama (local), Azure OpenAI, DeepSeek | Multi-model routing |
| API | REST + GraphQL (HotChocolate 15) + SSE | Streaming Q&A support |
| MCP | ModelContextProtocol.AspNetCore | HTTP transport |
| Frontend | Angular 19 (Standalone + Signals) | Real-time SSE streaming UI |
| Auth | Azure Entra External ID (CIAM) | JWT-based user data isolation |
| Observability | OpenTelemetry | Structured logging + health checks |
| Deployment | Docker Compose (local) / Azure Container Apps | IaC in `/infra` (Bicep) |

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Ollama](https://ollama.com/) with:
  ```bash
  ollama pull bge-m3        # embedding
  ollama pull qwen3:8b      # chat
  ```
- [Node.js 24+](https://nodejs.org/) for the frontend
- [Docker](https://www.docker.com/) for containerised deployment

### Run Locally

```bash
# 1. Start Ollama
ollama serve

# 2. Start the API
cd src/Veda.Api && dotnet run

# 3. Start the frontend (new terminal)
cd src/Veda.Web && npm install && npm start
```

| Endpoint | URL |
|----------|-----|
| API | http://localhost:5126 |
| Frontend | http://localhost:4200 |
| Swagger | http://localhost:5126/swagger |
| GraphQL Playground | http://localhost:5126/graphql |
| MCP | http://localhost:5126/mcp |

### Docker Compose

```bash
docker compose up -d
# Optional: expose via Cloudflare Tunnel
docker compose --profile tunnel up -d
```

---

## Project Structure

```
VedaAide.NET/
├── src/
│   ├── Veda.Core/          # Domain models, all IXxx interfaces, options
│   ├── Veda.Services/      # RAG engine: ingest, retrieval, embedding, LLM routing
│   ├── Veda.Storage/       # EF Core, vector stores, semantic cache, sync state
│   ├── Veda.Prompts/       # Context Window Builder, Chain-of-Thought strategy
│   ├── Veda.Agents/        # Semantic Kernel ReAct agent, orchestration service
│   ├── Veda.MCP/           # MCP server tools
│   ├── Veda.Evaluation/    # Faithfulness / Relevancy / Recall scorers
│   ├── Veda.Api/           # ASP.NET Core: REST + GraphQL + SSE + MCP
│   └── Veda.Web/           # Angular 19 frontend
├── tests/
│   ├── Veda.Core.Tests/
│   └── Veda.Services.Tests/    # 167 tests, all passing
├── docs/
│   ├── rag-internals/      # 9 PlantUML architecture diagrams
│   ├── designs/            # Phase design docs + ADRs
│   └── insights/           # Engineering decision write-ups
├── infra/                  # Azure Bicep IaC
└── docker-compose.yml
```

---

## API Reference (Selected)

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/documents` | Ingest a document |
| `POST` | `/api/documents/upload` | Upload PDF / image (multimodal OCR) |
| `POST` | `/api/query` | RAG query → answer + sources + hallucination flag |
| `GET`  | `/api/querystream` | Streaming RAG via SSE |
| `POST` | `/api/orchestrate/query` | Agent-orchestrated Q&A (ReAct loop) |
| `POST` | `/api/datasources/sync` | Trigger data source connectors (Blob / FileSystem) |
| `POST` | `/api/feedback` | Record accept / reject / edit feedback |
| `POST` | `/api/governance/groups` | Create a knowledge-sharing group |
| `POST` | `/api/evaluation/run` | Run RAG evaluation harness |
| `GET`  | `/api/evaluation/reports` | Query evaluation results |
| `POST` | `/mcp` | MCP endpoint (VS Code Copilot / Claude Desktop) |
| `POST` | `/graphql` | GraphQL endpoint |

Full API: [Swagger](http://localhost:5126/swagger) when running locally.

---

## Running Tests

```bash
dotnet test                                         # all 167 tests
dotnet test --filter "Category!=Integration"        # unit tests only
dotnet test --collect:"XPlat Code Coverage"         # with coverage
./scripts/smoke-test.sh                             # smoke tests (API must be running)
```

---

## MCP Integration

Add to `.vscode/mcp.json` while the API is running:

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

Available tools: `search_knowledge_base` · `list_documents` · `ingest_document`

---

## Documentation

| Document | Description |
|----------|-------------|
| [System Architecture](docs/rag-internals/01-system-architecture.en.md) | Layer diagram + Azure infra |
| [Ingest Pipeline](docs/rag-internals/02-ingest-flow.en.md) | Chunking → embedding → dedup → versioning |
| [Query Pipeline](docs/rag-internals/03-query-flow.en.md) | Hybrid retrieval → RRF → context window → hallucination guard |
| [Storage & Retrieval](docs/rag-internals/04-storage-retrieval.en.md) | SQLite vs CosmosDB, semantic cache |
| [RAG Concept ↔ Code Map](docs/rag-internals/05-concept-code-map.en.md) | 30 standard RAG terms mapped to implementation |
| [Architecture Decision Records](docs/rag-internals/09-adr.en.md) | 7 key decisions with rationale |
| [Configuration Reference](docs/configuration/configuration.en.md) | All `appsettings` keys and env vars |
| [Azure Deployment](docs/rag-internals/08-azure-deployment.en.md) | Container Apps + CosmosDB + CI/CD |

> All docs are bilingual: `.en.md` (English) and `.cn.md` (Chinese).

---

## Implementation Progress

| Stage | Description | Status |
|-------|-------------|--------|
| Phase 0 | Solution scaffold, EF Core, DI | ✅ |
| Phase 1 | Core RAG: ingest + vector search + LLM Q&A | ✅ |
| Phase 2 | RAG quality: dedup + hallucination detection | ✅ |
| Phase 3 | Full-stack: GraphQL + SSE streaming + Angular + Docker | ✅ |
| Phase 4 | Agentic workflow + MCP server + prompt engineering | ✅ |
| Phase 5 | External data sources (FileSystem + Blob), background sync | ✅ |
| Phase 6 | Evaluation harness: faithfulness, relevancy, A/B testing | ✅ |
| Stage 3.1 | KnowledgeScope + hybrid retrieval (RRF fusion) | ✅ |
| Stage 3.2 | Rich document extraction: Document Intelligence OCR + Vision multimodal | ✅ |
| Stage 3.3 | Structured reasoning output + knowledge versioning + semantic enhancer | ✅ |
| Stage 3.4 | Implicit feedback learning + multi-user knowledge governance (4-tier) | ✅ |
| Stage 5 | Azure Entra External ID CIAM + JWT-based user data isolation | ✅ |
| Stage 6 | Token usage tracking, email ingestion (EML/MSG), admin role isolation | ✅ |
| Stage 7 | Context Augmentation: ephemeral file/image injection without DB write | ✅ |


---

## Project Overview

VedaAide.NET is a full-stack AI knowledge base system built on .NET 10 and Semantic Kernel. It supports local document ingestion, semantic search, LLM-driven Q&A with hallucination detection, MCP (Model Context Protocol) integration, and agentic workflows.

**Key features:**
- Private deployment — runs entirely locally with Ollama (no cloud API required)
- Streaming Q&A via Server-Sent Events
- Dual-layer deduplication + dual-layer hallucination detection
- Agent orchestration with IRCoT (Interleaved Retrieval + Chain-of-Thought)
- MCP server (exposed) + MCP client (Azure Blob / FileSystem data sources)
- Content-hash-based incremental sync — skips unchanged files
- Angular 19 frontend with real-time streaming UI

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, ASP.NET Core, EF Core 10 + SQLite |
| AI Orchestration | Semantic Kernel 1.73 |
| LLM / Embedding | Ollama (local), DeepSeek / Azure OpenAI (cloud) |
| API | GraphQL (HotChocolate 15) + REST + SSE |
| Frontend | Angular 19 (Standalone + Signals API) |
| MCP | ModelContextProtocol.AspNetCore (HTTP transport) |
| Auth | Azure Entra External ID (CIAM) + MSAL Angular 3 |
| Cloud | Azure Blob Storage, Azure Container Apps |
| Deployment | Docker Compose (local) / Azure Container Apps (cloud) |

---

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Ollama](https://ollama.com/) with models pulled:
  ```bash
  ollama pull bge-m3        # embedding model
  ollama pull qwen3:8b      # chat model
  ```
- [Node.js 24+](https://nodejs.org/) (for frontend)
- [Docker](https://www.docker.com/) (for containerized deployment)

### Run Locally

```bash
# Start Ollama (if not already running as a service)
ollama serve

# Start the API
cd src/Veda.Api
dotnet run

# Start the frontend (separate terminal)
cd src/Veda.Web
npm install
npm start
```

API: http://localhost:5126  
Frontend: http://localhost:4200  
GraphQL Playground: http://localhost:5126/graphql  
Swagger: http://localhost:5126/swagger

### Run with Docker Compose

```bash
docker compose up -d
```

This starts: `veda-api` + `veda-web` + `ollama` (`cloudflared` is opt-in via `--profile tunnel` for local public exposure).

---

## Project Structure

```
VedaAide.NET/
├── src/
│   ├── Veda.Core/          # Domain models, interfaces, shared utilities
│   ├── Veda.Services/      # RAG engine, embedding, LLM, data source connectors
│   ├── Veda.Storage/       # EF Core DbContext, vector store, sync state
│   ├── Veda.Prompts/       # Prompt templates, context window builder, CoT
│   ├── Veda.Agents/        # LLM orchestration (Semantic Kernel agents)
│   ├── Veda.MCP/           # MCP server tools (knowledge base + ingest)
│   └── Veda.Api/           # ASP.NET Core API (REST + GraphQL + SSE + MCP)
├── tests/
│   ├── Veda.Core.Tests/
│   └── Veda.Services.Tests/
├── docs/
│   ├── configuration/      # Configuration reference (this section)
│   ├── designs/            # Architecture and phase design docs
│   ├── insights/           # Engineering insights and design decisions
│   └── tests/              # Test strategy and conventions
├── cloudflare/             # Cloudflare Tunnel configuration
└── docker-compose.yml
```

---

## Documentation

| Document | Description |
|----------|-------------|
| [docs/configuration/configuration.en.md](docs/configuration/configuration.en.md) | Full configuration reference (appsettings, env vars, User Secrets) |
| [docs/designs/system-design.en.md](docs/designs/system-design.en.md) | Architecture overview and phase-by-phase technical roadmap |
| [docs/designs/phase4-mcp-agents.en.md](docs/designs/phase4-mcp-agents.en.md) | Phase 4/5 design: MCP, Agent orchestration, Prompt engineering |
| [docs/tests/README.en.md](docs/tests/README.en.md) | Test strategy overview |
| [docs/tests/test-conventions.en.md](docs/tests/test-conventions.en.md) | Test naming conventions and coding standards |
| [docs/insights/README.en.md](docs/insights/README.en.md) | Engineering insights index |
| [cloudflare/README.md](cloudflare/README.md) | Cloudflare Tunnel setup guide |
| **RAG Internals (PlantUML diagrams)** | |
| [docs/rag-internals/PLAN.en.md](docs/rag-internals/PLAN.en.md) | RAG internals document index |
| [docs/rag-internals/01-system-architecture.en.md](docs/rag-internals/01-system-architecture.en.md) | System architecture: 6-project layering + Azure infra (PlantUML) |
| [docs/rag-internals/02-ingest-flow.en.md](docs/rag-internals/02-ingest-flow.en.md) | Ingest pipeline: chunking, embedding, dedup, versioning (PlantUML) |
| [docs/rag-internals/03-query-flow.en.md](docs/rag-internals/03-query-flow.en.md) | Query pipeline: hybrid retrieval, rerank, CoT, hallucination guard (PlantUML) |
| [docs/rag-internals/04-storage-retrieval.en.md](docs/rag-internals/04-storage-retrieval.en.md) | Storage layer: SQLite vs CosmosDB, vector search, semantic cache (PlantUML) |
| [docs/rag-internals/05-concept-code-map.en.md](docs/rag-internals/05-concept-code-map.en.md) | RAG concept ↔ code mapping table (30 standard terms) |
| [docs/rag-internals/06-module-dependencies.en.md](docs/rag-internals/06-module-dependencies.en.md) | Module dependency topology + DI registration (PlantUML) |
| [docs/rag-internals/07-data-model-er.en.md](docs/rag-internals/07-data-model-er.en.md) | Data model ER diagram: all entities and relationships (PlantUML) |
| [docs/rag-internals/08-azure-deployment.en.md](docs/rag-internals/08-azure-deployment.en.md) | Azure deployment: Container Apps, CosmosDB, CI/CD (PlantUML) |
| [docs/rag-internals/09-adr.en.md](docs/rag-internals/09-adr.en.md) | Architecture Decision Records: 7 key decisions |

> All docs are maintained in two languages: `.cn.md` (Chinese) and `.en.md` (English).

---

## Key API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/documents` | Ingest a document into the knowledge base |
| `POST` | `/api/documents/upload` | Upload an image/PDF (multimodal) into the knowledge base |
| `POST` | `/api/query` | RAG query (answer + sources + hallucination flag + optional structuredOutput) |
| `GET` | `/api/querystream` | Streaming RAG query via SSE |
| `POST` | `/api/querystream` | Streaming RAG query with ephemeral file context (Context Augmentation) |
| `POST` | `/api/context/extract` | Extract text from an uploaded file (ephemeral, no DB write) |
| `POST` | `/api/orchestrate/query` | Agent-orchestrated Q&A |
| `POST` | `/api/orchestrate/ingest` | Agent-orchestrated ingestion |
| `POST` | `/api/datasources/sync` | Manually trigger all enabled data source connectors |
| `POST` | `/api/feedback` | Record a user behavior event (accept/reject/edit/click) |
| `GET` | `/api/feedback/stats` | Get feedback statistics (frequently rejected chunks) |
| `POST` | `/api/governance/groups` | Create a sharing group (family/team) |
| `PUT` | `/api/governance/documents/{id}/share` | Authorize document sharing |
| `GET` | `/api/governance/consensus/pending` | List pending consensus candidates |
| `POST` | `/api/governance/consensus/{id}/review` | Review a consensus candidate (admin) |
| `GET` | `/api/governance/documents/{id}/visible` | Check document visibility for a user |
| `GET` | `/api/admin/stats` | Vector store stats (Admin Key required) |
| `GET` | `/api/admin/chunks` | Paginated vector chunk browser (Admin Key required) |
| `GET` | `/api/admin/documents/{name}/history` | Document version history |
| `DELETE` | `/api/admin/data` | Clear all vector data (requires `X-Confirm: yes`) |
| `DELETE` | `/api/admin/cache` | Clear semantic cache |
| `DELETE` | `/api/admin/documents/{id}` | Delete a document |
| `GET` | `/api/prompts` | List prompt templates |
| `POST` | `/api/prompts` | Create / update a prompt template |
| `POST` | `/mcp` | MCP endpoint (for VS Code Copilot / other MCP clients) |
| `POST` | `/graphql` | GraphQL endpoint |

---

## Configuration

See [docs/configuration/configuration.en.md](docs/configuration/configuration.en.md) for the full reference.

Key settings in `src/Veda.Api/appsettings.json`:

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

Sensitive values (e.g., `BlobStorage:ConnectionString`) should be stored in User Secrets or environment variables — never in `appsettings.json`.

---

## Running Tests

```bash
# All unit tests
dotnet test

# With coverage
dotnet test --collect:"XPlat Code Coverage"

# Smoke tests (requires API running)
./scripts/smoke-test.sh
```

Current test count: **167 tests**, all passing.

---

## MCP Integration (VS Code Copilot)

If VedaAide API is running, add to `.vscode/mcp.json`:

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

Available tools: `search_knowledge_base`, `list_documents`, `ingest_document`

---

## Implementation Phases

| Phase | Description | Status |
|-------|-------------|--------|
| Phase 0 | Solution scaffold, EF Core, DI setup | ✅ Done |
| Phase 1 | Core RAG engine (ingest + vector search + LLM Q&A) | ✅ Done |
| Phase 2 | RAG quality (dedup + hallucination detection + reranking) | ✅ Done |
| Phase 3 | Full-stack (GraphQL + SSE streaming + Angular frontend + Docker) | ✅ Done |
| Phase 4 | Agentic workflow + MCP server + Prompt engineering | ✅ Done |
| Phase 5 | External data sources (FileSystem + Blob), background sync, sync state tracking | ✅ Done |
| Phase 6 | AI evaluation harness (faithfulness, relevancy, A/B testing) | ✅ Done |
| Stage 3 Sprint 1 | KnowledgeScope + hybrid retrieval dual-channel (RRF fusion) | ✅ Done |
| Stage 3 Sprint 2 | Rich document extraction (Document Intelligence OCR + Vision multimodal) | ✅ Done |
| Stage 3 Sprint 3 | Structured reasoning output + knowledge versioning + semantic enhancer | ✅ Done |
| Stage 3 Sprint 4 | Implicit feedback learning + multi-user knowledge governance (4-tier model) | ✅ Done |
| Stage 5 | User authentication (Azure Entra External ID CIAM) + full route protection (MsalGuard) + JWT-based user data isolation | ✅ Done |
| Stage 6 | Rich document extraction quality (Certificate type, PDF text layer, Azure DI quota awareness, Ollama Vision provider, token usage stats, email ingestion EML/MSG, admin role isolation) | ✅ Done |
| Stage 7 Phase 4 | Context Augmentation (Ephemeral RAG): upload file / paste image in chat, extract text without DB write, inject into LLM prompt | ✅ Done |
