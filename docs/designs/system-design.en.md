# Project Design Overview: VedaAide .NET

> 💡 Non-obvious design decisions accumulated during development: [docs/insights/](../insights/README.en.md)

> 中文版见 [system-design.cn.md](system-design.cn.md)

## 1. Project Overview

- Project name: VedaAide .NET
- Core positioning: General-purpose, enterprise-grade, private-deployable RAG intelligent Q&A system
- Backend stack: .NET 10 (C#) + ASP.NET Core + EF Core 10 + Semantic Kernel 1.73 + HotChocolate 15 (GraphQL)
- AI stack: Ollama (local Embedding) + DeepSeek / Azure OpenAI (LLM)
- Frontend stack: Angular + TypeScript
- API stack: GraphQL (HotChocolate) + REST
- Storage stack: SQLite-VSS (vector) + SQLite/SQL Server via EF Core (relational data)
- Cloud stack: Azure Blob Storage + Azure OpenAI + Azure Container Apps
- Project goal: General-purpose private knowledge base, balancing local privacy with cloud extensibility
- Demo scenario: Externally accessible (local server + Cloudflare Tunnel / Azure Container Apps)

## 2. Core Architecture Design

Hybrid local + cloud architecture maximizes cost control and privacy.

🏛️ System Architecture (Logical Layers)

1. Frontend Layer
- Angular + TypeScript SPA
- Supports document upload, conversational Q&A, result display, evaluation report viewing
- Communicates with backend via GraphQL (HotChocolate)

2. API Gateway Layer
- ASP.NET Core Web API
- GraphQL endpoint (HotChocolate)
- REST endpoint (compatibility / Webhook)

3. Core Business Layer
- RAG engine: document processing, chunking, retrieval, generation
- Deduplication engine: hash + similarity-based
- Anti-hallucination validation: vector check + LLM self-check
- Prompt Engineering module: prompt template management, context window optimization
- AI evaluation module: output quality scoring, cross-model comparison

4. Agent Orchestration Layer
- Microsoft Agent Framework: multi-Agent collaborative workflows
- MCP (Model Context Protocol): standardized tool calling and external data source integration
- Semantic Kernel: LLM orchestration and Plugin system

5. AI Services Layer
- Local Embedding: Ollama (bge-m3 / mxbai)
- Cloud LLM: DeepSeek Chat / Azure OpenAI
- Azure AI Search (optional, cloud vector retrieval)

6. Data Storage Layer
- Vector store: SQLite-VSS (local) / Azure AI Search (cloud)
- Relational data: EF Core + SQLite (local) / SQL Server (cloud extension)
  - Stores: user sessions, document metadata, evaluation records, prompt template versions
- Document store: local file system / Azure Blob Storage


## 3. Technical Roadmap


🏗️ Phase 0: Solution Bootstrap ✅ Done

1. Create .NET Solution and all projects:
   - `Veda.Core`, `Veda.Services`, `Veda.Storage`, `Veda.Prompts`
   - `Veda.Agents`, `Veda.MCP`, `Veda.Api`, `Veda.Web`
   - Test projects: `Veda.Core.Tests`, `Veda.Services.Tests`
2. Configure EF Core + `VedaDbContext` (Code-First, SQLite):
   - Initial migration: `VectorChunks`, `PromptTemplates` tables (`SyncedFiles` added in Phase 5).
3. Configure base DI container, configuration files (`appsettings.json`), logging.
4. Build `Veda.Api` (ASP.NET Core Web API) minimal skeleton, verify startup.


🚀 Phase 1: Core RAG Engine (MVP) ✅ Done

1. Document processing pipeline
   - Priority: Txt / Markdown; extensible to PDF / Word.
   - Dynamic chunking strategy:
     - Bill/Invoice: small granularity (256 tokens), precise field extraction.
     - Specification/PDS: large granularity (1024 tokens), preserve complete semantics.
2. Embedding strategy
   - Deploy local Ollama: `nomic-embed-text` (lightweight) or `mxbai-embed-large` (higher accuracy).
   - Wrapped via Semantic Kernel `OllamaTextEmbeddingGenerationService`.
   - Key: zero cost, no network latency, private.
3. Vector storage
   - Use `sqlite-vec` + `Microsoft.Data.Sqlite`.
   - Wrapped as `VectorDbProvider`, interface-abstracted for easy switching.
4. RAG query end-to-end
   - `Veda.Api` exposes two REST endpoints: `POST /documents` (ingest), `POST /query` (Q&A).
   - Semantic Kernel orchestration: retrieve relevant chunks → build prompt → call Ollama LLM → return answer.


🔒 Phase 2: RAG Quality Enhancement ✅ Done

1. Smart deduplication module (dual-layer)
   - **Layer 1 — Hash dedup**: SHA-256 of chunk content; skip if already exists, preventing identical content from being ingested twice.
   - **Layer 2 — Vector similarity dedup**: For each new chunk, vector search before storage; if cosine similarity ≥ `SimilarityDedupThreshold` (default 0.95) with any existing content, treat as semantic duplicate and skip.

2. Dual hallucination detection
   - **Layer 1 — Answer Embedding Check**: Embed the LLM answer → search vector store → if max similarity < `HallucinationSimilarityThreshold` (default 0.3), set `IsHallucination = true`. Answer still returned, only flagged.
   - **Layer 2 — LLM Self-Check**: `IHallucinationGuardService.VerifyAsync()` calls LLM again for fact-checking. Controlled by `Veda:EnableSelfCheckGuard: true/false`.

3. RAG retrieval optimization
   - **Reranking**: Retrieve `2 × TopK` candidates; `QueryService.Rerank()` (private static, 70% vector + 30% keyword score) re-scores and selects top `TopK`. No separate interface currently.
   - **Date-range metadata filter**: `RagQueryRequest.DateFrom`/`DateTo` filters `CreatedAtTicks` in `WHERE` clause.


🌐 Phase 3: API Layer + Frontend ✅ Done

1. `Veda.Api` completion
   - **HotChocolate 15** GraphQL endpoint (`/graphql`): `Query.AskAsync`, `Mutation.IngestDocumentAsync`.
   - **SSE streaming** `GET /api/querystream`: pushes `{type:"sources"}` → `{type:"token", token:"..."}` → `{type:"done", ...}`.
   - `OllamaChatService` implements token-level streaming via SK `GetStreamingChatMessageContentsAsync`.
2. `Veda.Web` (Angular 19 Standalone + Signals API)
   - **Shell**: sidebar navigation, lazy-loaded routes (`/chat`, `/ingest`, `/prompts`).
   - **Ingest page** (`/ingest`): Notes / Documents tabs; Notes Tab for direct text ingestion, Documents Tab for file upload; shared ingestion history table with status badges.
   - **Chat page** (`/chat`, default route): streaming Q&A, message bubbles, collapsible source citation panel, hallucination warning badge, confidence display, real-time typing cursor animation.
   - **Prompts page** (`/prompts`): Phase 4 placeholder, Phase 4.5 upgraded to full CRUD.
   - Dev proxy: `/api` and `/graphql` forwarded to `localhost:5126` via `proxy.conf.json`.
3. Deployment
   - `Dockerfile` (API + Web): multi-stage builds, SQLite data directory mounted as Volume.
   - `docker-compose.yml`: one-command startup of `veda-api` + `veda-web` + `ollama` + `cloudflared`.
   - **Plan A (implemented)**: `cloudflare/config.yml` + `cloudflare/README.md`, complete Cloudflare Tunnel setup.
   - Plan B (reserved): Azure Container Apps with auto-scaling.


🤖 Phase 4: Agentic Workflow + Prompt Engineering ✅ Done

1. Agent orchestration foundation (deterministic call chain) ✅
- `OrchestrationService`: serial deterministic chain, three embedded roles:
  - DocumentAgent: filename → `DocumentType` inference → `DocumentIngestService.IngestAsync()`
  - QueryAgent: `QueryService.QueryAsync()` → returns `agentTrace`
  - EvalAgent: `HallucinationGuardService.VerifyAsync()` → context consistency check
- REST endpoints: `POST /api/orchestrate/query`, `POST /api/orchestrate/ingest`

2. Agent orchestration upgrade (LLM-driven) ✅ (`LlmOrchestrationService`)
- `LlmOrchestrationService`: uses `ChatCompletionAgent` + `VedaKernelPlugin` (`search_knowledge_base` KernelFunction).
- LLM autonomously decides which tools to call and how many times (Reason-Act-Observe loop), implementing IRCoT.
- `AgentServiceExtensions.AddVedaAgents()` registers `LlmOrchestrationService` as the default implementation.

3. MCP (Model Context Protocol) Bidirectional Integration
- **VedaAide as MCP Server** ✅ (`Veda.MCP` project):
  - Exposes `search_knowledge_base`, `list_documents`, `ingest_document` tools
  - External AI clients (VS Code Copilot, etc.) connect via HTTP to `/mcp` endpoint
- **VedaAide as MCP Client** ✅:
  - `IDataSourceConnector` interface (`Veda.Core.Interfaces`)
  - `FileSystemConnector`: local directory batch ingestion
  - `BlobStorageConnector`: Azure Blob Storage ingestion

4. Prompt / Context Engineering ✅
- ✅ Versioned prompt template library: `PromptTemplateRepository` (EF Core + SQLite)
- ✅ Dynamic context window trimming: `ContextWindowBuilder.Build()`, token budget greedy selection
- ✅ System prompt loaded from database (`"rag-system"` template, fallback to hardcoded default)
- ✅ Chain-of-Thought prompt strategy: `ChainOfThoughtStrategy.Enhance()`, injected into `QueryService`


🔗 Phase 5: External Data Sources + Sync State Tracking ✅ Done

1. MCP Client: External data source integration ✅
- `IDataSourceConnector` interface ✅
- `FileSystemConnector`: reads local directory, batch ingests to knowledge base ✅
- `BlobStorageConnector`: Azure Blob Storage data source ✅
- `POST /api/datasources/sync`: manually trigger all enabled data source syncs ✅
- `DataSourceSyncBackgroundService`: scheduled auto-sync ✅

2. Sync state tracking ✅
- `ISyncStateStore` / `SyncStateStore` (EF Core + `SyncedFiles` table, migration `Phase5_SyncedFiles`)
- SHA-256 content hash stored per (ConnectorName, FilePath) tuple
- Both connectors skip files whose content hash hasn't changed since last sync

3. Prompts Management UI ✅
- `PromptsComponent` (Angular, `/prompts` route): full CRUD — view version list, create, edit, delete templates.
- REST endpoints: `GET /api/prompts`, `POST /api/prompts`, `DELETE /api/prompts/{id}`.


📊 Phase 6: AI Evaluation System ⏳ **Planned — Not Yet Implemented**

1. Evaluation metrics
- Faithfulness: does the answer rely only on retrieved context?
- Answer Relevancy: is the answer on-topic?
- Context Recall: were the relevant document chunks retrieved?
- BLEU / ROUGE: text similarity to reference answers (optional).

2. Automated Test Harness
- Maintain a Golden Dataset: standard question set + expected answers.
- Auto-evaluate after every model/prompt change, output comparison report.
- A/B testing: compare scores across different models or prompt versions for the same question.

3. Integration testing strategy
- RAG pipeline end-to-end integration tests (NUnit).
- Deterministic boundary tests for AI output (verifying hallucination protection works).
- Use mock LLM for fast, low-cost unit tests.
