> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[09-adr.cn.md](09-adr.cn.md)

# 09 — Architecture Decision Records (ADR)

> Records the context, alternatives, final choices, and trade-off rationale for key technical decisions in VedaAide.  
> Format: Context → Options → Decision → Consequences.

---

## ADR-001: Dual Vector Store Implementation (SQLite / CosmosDB)

**Status**: Accepted  
**Date**: Phase 1 (SQLite) → Phase 4 (CosmosDB extension)

### Context
Need to persistently store document vectors and support similarity retrieval, while accommodating both local development experience and production-scale requirements.

### Options
| Option | Pros | Cons |
|--------|------|------|
| Azure AI Search only | Feature-rich, good integration | Internet required for local dev, costs money |
| pgvector (PostgreSQL) only | Open-source, exact ANN | Requires maintaining a PostgreSQL instance |
| **SQLite (dev) + CosmosDB (prod)** | Zero-dependency local dev; production DiskANN performance | Dual-implementation maintenance cost |
| In-memory only | Extremely simple | Data lost on restart, unusable in production |

### Decision
Pluggable dual implementation via `IVectorStore` interface + `Veda:StorageProvider` config.  
SQLite uses in-memory full cosine scan (exact), suitable for <100K chunks; CosmosDB uses DiskANN approximate index, suitable for millions of documents.

### Consequences
- ✅ Local dev requires zero infrastructure — `dotnet run` is enough
- ✅ Seamless production migration by changing a single config value
- ✅ Interface design enforces DIP — dependencies on abstractions, not implementations
- ⚠️ SQLite full scan degrades at >100K chunks
- ⚠️ Both implementations must be kept in sync

---

## ADR-002: Hybrid Retrieval Fusion Uses RRF Instead of WeightedSum

**Status**: Accepted (default); WeightedSum retained as option  
**Date**: Phase 2 Sprint 1

### Context
Hybrid retrieval (vector + keyword) produces two ranked result lists that need to be merged. Vector similarity and keyword scores have different scales (former [-1,1], latter is term frequency count), so direct addition requires normalization.

### Options
| Option | Description | Issue |
|--------|-------------|-------|
| **RRF (Reciprocal Rank Fusion)** | `score += 1/(60+rank)`, rank-only, score-agnostic | Near-optimal but ignores score magnitude |
| WeightedSum | `score = 0.7 × vectorSim + 0.3 × keywordScore` | Requires normalization; different score ranges make weights unreliable |
| Borda Count | Rank-based voting | Complex implementation, similar effect to RRF |

### Decision
Default to RRF, k=60 (standard value). The `FusionStrategy` enum retains `WeightedSum` for users to switch (`Veda:Rag:FusionStrategy`).

### Rationale
- RRF is the mainstream choice in both academic research and industry for hybrid retrieval (empirically superior)
- No score normalization needed; robust to uneven quality between channels
- k=60 effectively suppresses head-concentration (long-tail documents get a chance)

### Consequences
- ✅ Good results with no tuning needed
- ✅ Clean code, no floating-point normalization logic
- ⚠️ Gives up absolute score information (an extremely high-similarity result gets no extra boost)

---

## ADR-003: Two-Layer Hallucination Guard Instead of Single Layer

**Status**: Accepted  
**Date**: Phase 2

### Context
LLMs often generate answers that sound plausible but don't align with knowledge base content (hallucinations). Need to detect and flag these without significantly increasing latency.

### Options
| Option | Accuracy | Latency | Cost |
|--------|----------|---------|------|
| No check | — | 0ms | 0 |
| Embedding similarity only | Low (high false positive rate) | ~100ms | Low |
| LLM self-check only | High | +1 LLM call | High |
| **Embedding check + optional LLM check** | High | On demand | On demand |

### Decision
- **Layer 1** (mandatory): Answer embedding vs. vector store max similarity < 0.3 → hallucination. Fast coarse filter, no extra LLM call.
- **Layer 2** (optional, `EnableSelfCheckGuard=false` by default): LLM verifies line-by-line "Is the Answer fully supported by the Context?". High precision at the cost of one extra LLM call per query.

### Consequences
- ✅ Production default uses Layer 1 only — negligible latency impact
- ✅ Layer 2 can be enabled for high-confidence scenarios
- ⚠️ Layer 1 threshold 0.3 is low; coverage over precision (prefer false negative over missing a valid answer)
- ⚠️ Layer 2 doubles query cost when enabled

---

## ADR-004: Semantic Cache Disabled by Default, Enabled via Config

**Status**: Accepted  
**Date**: Phase 2 Sprint 3

### Context
Caching LLM answers for semantically similar repeated questions can dramatically reduce cost and latency, but when knowledge base content changes frequently, cached answers may become stale.

### Decision
- Semantic cache **disabled by default** (`Veda:SemanticCache:Enabled: false`)
- Hit threshold 0.95 (only reuse on extremely similar questions)
- Clear the entire cache synchronously after successful ingestion (`ClearAsync()`)
- TTL default: 1 hour

### Trade-offs
| Concern | Design Choice |
|---------|--------------|
| Answer accuracy | Clear cache on content change; accuracy takes priority |
| Cache granularity | Full clear (simple and reliable) rather than precise invalidation |
| Hit threshold | 0.95 (very strict) to avoid mis-hits on semantically different questions |

### Consequences
- ✅ Highly effective when knowledge base is stable (0 LLM calls for repeated questions)
- ⚠️ Frequent ingestion (e.g. hourly batch sync) causes frequent cache invalidations
- ⚠️ Full clear has a performance cost with large cache volumes (DeleteAll SQL)

---

## ADR-005: Chunking Differentiated by DocumentType Rather Than Fixed Size

**Status**: Accepted  
**Date**: Phase 1

### Context
Different document types have vastly different information density: each line of an invoice is an independent field (smaller chunks = more precise), while a specification document needs surrounding context (larger chunks = more complete). Fixed chunk size degrades retrieval quality.

### Decision
`ChunkingOptions.ForDocumentType()` returns different `(TokenSize, OverlapTokens)` per document type.

| DocumentType | TokenSize | OverlapTokens | Rationale |
|--------------|-----------|---------------|-----------|
| BillInvoice  | 256 | 32 | Each field is independent; small chunks give precise amount/date matching |
| PersonalNote | 256 | 32 | Notes are short; small chunks are appropriate |
| Report / Other | 512 | 64 | General size; balances context and precision |
| RichMedia | 512 | 64 | Vision-extracted text is typically paragraph-length |
| Specification | 1024 | 128 | Technical clauses need large windows to retain technical context |

### Consequences
- ✅ Invoice query precision significantly improved (no mixing amounts from two invoices in one chunk)
- ✅ Specification queries retain sufficient context
- ⚠️ Type inference relies on filename heuristics (`DocumentTypeParser.InferFromName`), which may be inaccurate

---

## ADR-006: IRCoT (LLM-Decided Retrieval) Instead of Fixed Single Retrieval

**Status**: Accepted (Phase 4, as Agent mode)  
**Date**: Phase 4

### Context
Complex questions may require multiple retrievals: the first retrieval provides background, then more targeted queries are refined based on that background, and so on. A fixed "single retrieval → generate" pipeline cannot handle these cases.

### Options
| Option | Description |
|--------|-------------|
| Fixed single retrieval RAG | Simple, low latency |
| Query Decomposition | Pre-decompose complex questions, retrieve separately |
| **IRCoT (Interleaved Retrieval CoT)** | LLM autonomously decides when to retrieve, alternating reasoning and retrieval |
| ReAct Agent | Similar to IRCoT; LLM generates Thought + Action + Observation |

### Decision
Phase 4 introduces `LlmOrchestrationService`, using Semantic Kernel `ChatCompletionAgent` + `VedaKernelPlugin` (`search_knowledge_base` KernelFunction), with `FunctionChoiceBehavior.Auto()` letting the LLM autonomously decide when and how many times to call tools.

The original `OrchestrationService` (fixed manual chain, single retrieval) is retained; both are injected via DI as different implementations.

### Consequences
- ✅ Significantly better quality for complex multi-hop questions
- ✅ LLM can refine search terms based on intermediate results
- ⚠️ Variable loop count; latency and cost are non-deterministic
- ⚠️ Hard to debug (requires AgentTrace to record each step)

---

## ADR-007: MCP Server Instead of Proprietary API Integration

**Status**: Accepted  
**Date**: Phase 4

### Context
As external LLMs (Claude, GPT-4, etc.) grow more capable, enabling them to directly access VedaAide's knowledge base becomes valuable. Each LLM has a different tool-call format, so proprietary integration requires writing an adapter per LLM.

### Decision
Adopt the **Model Context Protocol (MCP)** standard. The `Veda.MCP` project exposes a standardized MCP Server (HTTP/SSE) with three tools:
- `search_knowledge_base`
- `ingest_document`
- `list_documents`

### Consequences
- ✅ Any MCP-compatible LLM/tool (Claude Desktop, Cursor, etc.) can connect directly
- ✅ VedaAide is simultaneously an MCP Server (exposes capabilities) and an MCP Client (`DataSourceConnector` pulls from external sources)
- ⚠️ MCP protocol is still evolving rapidly; needs to track standard updates
