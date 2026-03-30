# VedaAide.NET

A general-purpose, enterprise-grade, private-deployable RAG (Retrieval-Augmented Generation) intelligent Q&A system.

> 中文文档见 [README.cn.md](README.cn.md)

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

Current test count: **134 tests**, all passing.

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
