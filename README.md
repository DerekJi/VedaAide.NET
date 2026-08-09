# VedaAide.NET

> **Production-grade RAG (Retrieval-Augmented Generation) platform** — built from scratch in C# / .NET 10 with Semantic Kernel.
> Hybrid retrieval, multi-layer hallucination defence, agent orchestration, MCP integration, enterprise auth, and a quantitative evaluation harness.
> This is a personal portfolio project demonstrating end-to-end AI systems engineering.

> Chinese version: [README.cn.md](README.cn.md)

---

## Why I Built This

Most RAG tutorials stop at "embed → store → search → answer." That is not enough for production. This project is my answer to a real question I kept asking as an AI engineer: *what does a genuinely production-ready RAG system actually look like?*

The goal was never a demo — it was to design and ship a **complete, deployable AI product** the way I would build it at work:

- **Reason about trade-offs, not just glue APIs.** Every major decision (hybrid retrieval, dual-layer hallucination defence, semantic cache, multi-model routing, ephemeral context) is documented as an [Architecture Decision Record](docs/rag-internals/09-adr.en.md) with the alternatives considered and the reasons chosen.
- **Engineer for real users.** Authentication, per-user data isolation, admin role separation, rate limiting, token-usage tracking, feedback loops, and a deployed CI/CD pipeline on Azure.
- **Make the system measurable.** An evaluation harness with LLM-as-a-judge scorers so retrieval and answer quality can be compared quantitatively, not by vibes.
- **Integrate with the AI tooling ecosystem.** The knowledge base is exposed as an MCP (Model Context Protocol) server, so it plugs into VS Code Copilot, Claude Desktop, or any MCP-compatible assistant with one config line.

The result is a working, deployed RAG platform with **170+ automated tests**, bilingual engineering documentation, and an honest account of what is done and what remains.

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

Eight layered C# projects with strict dependency direction: `Core → Services → Storage → Entry Points`.
See the [full module dependency diagram](docs/rag-internals/06-module-dependencies.en.md).

### Key Engineering Decisions

**1. Hybrid retrieval with RRF fusion.** Dense vector search (cosine similarity) and sparse keyword search run concurrently and are merged with **Reciprocal Rank Fusion (RRF, k=60)** — mathematically sound, no tuning required. `WeightedSum` and `RRF` strategies are both supported and configurable. Keyword search significantly outperforms dense retrieval on exact terms, product codes, and proper nouns; hybrid covers both failure modes.

**2. Dual-layer hallucination defence.** Layer 1: the LLM generates an answer plus a self-check confidence flag in a single structured call. Layer 2: `HallucinationGuardService` sends the answer + retrieved context to an independent second LLM call as a fact-checker. Configurable via `Veda:Rag:EnableSelfCheckGuard`; adds ~300ms but eliminates unsupported claims.

**3. Semantic cache (CosmosDB + SQLite).** Incoming questions are compared against cached embeddings via cosine similarity *before* calling the embedding model or LLM, with a configurable similarity threshold — two implementations behind one interface.

**4. LLM router.** Selects the model by `QueryMode`: `Simple` → lightweight model (Ollama local / GPT-4o-mini), `Advanced` → DeepSeek (or any OpenAI-compatible endpoint), with graceful fallback when the advanced model is not configured.

**5. Token-aware context window.** `ContextWindowBuilder` selects chunks by similarity score and enforces a strict token budget (conservative 3 chars/token estimate for mixed Chinese/English content), preventing low-relevance chunks from polluting the LLM window.

**6. ReAct agent (Semantic Kernel plugin).** `VedaKernelPlugin` exposes knowledge-base retrieval as a `[KernelFunction]`; the SK `ChatCompletionAgent` runs a **Reason–Act–Observe** loop, deciding *when* and *what* to retrieve rather than retrieval being hardcoded into the query path.

**7. MCP server.** Three tools over HTTP transport: `search_knowledge_base`, `list_documents`, `ingest_document` — protected by API Key, consumed by VS Code Copilot / Claude Desktop.

**8. Quantitative RAG evaluation.** Three LLM-as-a-judge scorers:

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
| Backend | .NET 10, ASP.NET Core, EF Core 10 | Clean architecture, 8 projects |
| AI Orchestration | Semantic Kernel 1.73 | Plugin-based ReAct agent |
| Vector DB | Azure CosmosDB (DiskANN) / SQLite-VSS | Pluggable via `IVectorStore` |
| LLM / Embedding | Ollama (local), Azure OpenAI, DeepSeek | Multi-model routing + fallback |
| API | REST + GraphQL (HotChocolate 15) + SSE | Streaming Q&A, multimodal ingest |
| MCP | ModelContextProtocol.AspNetCore | HTTP transport |
| Frontend | Angular 19 (Standalone + Signals) | Real-time SSE streaming UI |
| Auth | Azure Entra External ID (CIAM) + MSAL Angular 3 | JWT-based per-user data isolation |
| Observability | OpenTelemetry | Structured logging + health checks |
| Testing | NUnit + FluentAssertions + Moq | 170+ unit & integration tests |
| Deployment | Docker Compose (local) / Azure Container Apps | IaC in `/infra` (Bicep) + GitHub Actions |

---

## Development Progress (objective)

### Completed

| Phase / Stage | Description | Status |
|-------|-------------|--------|
| Phase 0–2 | Solution scaffold; core RAG (ingest + vector search + Q&A); dedup + hallucination detection | ✅ |
| Phase 3 | Full-stack: GraphQL + SSE streaming + Angular + Docker | ✅ |
| Phase 4–5 | Agentic workflow + MCP server + prompt engineering; external data sources (FileSystem + Blob) with background sync | ✅ |
| Phase 6 | AI evaluation harness: faithfulness, relevancy, A/B testing | ✅ |
| Stage 3.1–3.4 | KnowledgeScope + hybrid retrieval (RRF); Document Intelligence OCR + Vision multimodal; structured reasoning output + knowledge versioning + semantic enhancer; implicit feedback learning + 4-tier knowledge governance | ✅ |
| Stage 5 | Azure Entra External ID (CIAM) auth + MsalGuard route protection + JWT-based user data isolation | ✅ |
| Stage 6 | Token-usage tracking, email ingestion (EML/MSG), admin role isolation, Certificate/PDF text-layer extraction | ✅ |
| Stage 7 | Multi-session chat + backend persistence; Context Augmentation (ephemeral RAG — upload file / paste image, no DB write) | ✅ |
| Stage 8 | Public resume-tailoring endpoints (`/api/public/resume/*`) with per-IP rate limiting + CORS allowlist | ✅ |

**Current engineering baseline:**
- **170+ automated tests** (149 `[Test]` + 27 parametrized `[TestCase]`), all passing
- Deployed to **Azure Container Apps** with CosmosDB, managed identity, and CI/CD from `main`
- Bilingual docs: 9 PlantUML architecture diagrams, 7 ADRs, 10+ engineering insight write-ups

### Known gaps / next steps (honest list)

- `AdminController.Stats` does not yet surface semantic-cache hit-rate statistics
- Ingest does not invalidate the semantic cache automatically (manual clear today)
- MCP `list_documents` is safe and API-Key protected; broader MCP tooling (e.g. pricing / route plugins) is a planned epic
- Vector data does not auto-migrate between storage providers (SQLite ↔ CosmosDB) — switching requires re-ingestion; documented in the design docs
- Embedding model changes (dimension changes) require a data reset + re-ingest; no automatic detection yet

This list is kept deliberately — it reflects how the project is built and where the next round of work would go.

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
│   ├── Veda.Services.Tests/    # 170+ tests, all passing
│   └── Veda.Evaluation.Tests/
├── docs/
│   ├── rag-internals/      # 9 PlantUML architecture diagrams
│   ├── designs/            # Phase design docs + ADRs
│   ├── insights/           # Engineering decision write-ups
│   └── tests/              # Test strategy & conventions
├── infra/                  # Azure Bicep IaC
└── docker-compose.yml
```

---

## Key API Endpoints (selected)

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/documents` | Ingest a document |
| `POST` | `/api/documents/upload` | Upload PDF / image (multimodal OCR) |
| `POST` | `/api/query` | RAG query → answer + sources + hallucination flag |
| `GET`  | `/api/querystream` | Streaming RAG via SSE |
| `POST` | `/api/querystream` | Streaming RAG with ephemeral file context (Context Augmentation) |
| `POST` | `/api/context/extract` | Extract text from an uploaded file (ephemeral, no DB write) |
| `POST` | `/api/orchestrate/query` | Agent-orchestrated Q&A (ReAct loop) |
| `POST` | `/api/datasources/sync` | Trigger data source connectors (Blob / FileSystem) |
| `POST` | `/api/feedback` | Record accept / reject / edit feedback |
| `POST` | `/api/governance/groups` | Create a knowledge-sharing group |
| `POST` | `/api/evaluation/run` | Run RAG evaluation harness |
| `POST` | `/api/public/resume/tailor` | Public SSE resume tailoring (per-IP rate limited) |
| `POST` | `/mcp` | MCP endpoint (VS Code Copilot / Claude Desktop) |
| `POST` | `/graphql` | GraphQL endpoint |

Full API: [Swagger](http://localhost:5126/swagger) when running locally.

---

## Running Tests

```bash
dotnet test                                         # all 170+ tests
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
      "url": "http://localhost:5126/mcp",
      "headers": { "X-Api-Key": "<your-api-key>" }
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
| [Test Strategy](docs/tests/README.en.md) | Test conventions & naming standards |
| [Engineering Insights](docs/insights/README.en.md) | Chunking, anti-hallucination, reranking, MCP, and more |

> All docs are maintained bilingually: `.en.md` (English) and `.cn.md` (Chinese).
