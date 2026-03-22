# Local Ollama Embedding: Why Not Cloud APIs

**Project**: VedaAide.NET  
**Files**: `src/Veda.Services/EmbeddingService.cs`, `src/Veda.Services/ServiceCollectionExtensions.cs`  
**Phase**: Phase 1

> 中文版见 [embedding-local-ollama.cn.md](embedding-local-ollama.cn.md)

---

## Decision

Embedding uses local Ollama (`nomic-embed-text` / `bge-m3`) instead of OpenAI `text-embedding-ada-002` or Azure OpenAI.

---

## Why Not Cloud Embedding APIs

| Dimension | Cloud API | Local Ollama |
|---|---|---|
| Cost | Per-token billing — expensive for large ingestion batches | Zero marginal cost |
| Latency | Subject to network RTT — ~50–200ms/call | Local inference — ~5–30ms/call |
| Privacy | Document content leaves the machine — unsuitable for sensitive data | Data never leaves the local machine |
| Offline | Requires network | Fully offline capable |
| Model lock-in | Vendor model, no choice | Any GGUF model can be used |

For a private knowledge base scenario, **privacy and cost are decisive factors** — local embedding is the natural choice.

---

## Architecture Isolation: Not Depending on the Ollama SDK Directly

`EmbeddingService` depends on the standard `IEmbeddingGenerator<string, Embedding<float>>` interface from `Microsoft.Extensions.AI`, not on Ollama SDK concrete types:

```csharp
// EmbeddingService.cs — domain layer only depends on the standard interface, unaware of the underlying implementation
public sealed class EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> inner) : IEmbeddingService
```

Ollama registration happens in `ServiceCollectionExtensions.cs` via `.AddOllamaEmbeddingGenerator()`.

**Benefit:** Switching to Azure OpenAI Embedding or `bge-m3` in the future only requires changing one DI registration line — `EmbeddingService` and all upstream code are zero-change. This is **DIP (Dependency Inversion Principle)** applied to the AI service layer.

---

## Model Selection Trade-offs

| Model | Characteristics | Use Case |
|---|---|---|
| `nomic-embed-text` | Lightweight (~270 MB), English-first | English documents, resource-constrained environments |
| `bge-m3` | Larger (~570 MB), Chinese/English/Japanese multilingual | Chinese content, recommended for production |
| `mxbai-embed-large` | High accuracy for English | English professional documents |

`nomic-embed-text` was chosen initially to quickly validate the pipeline; for Chinese content, `bge-m3` should be used (top-ranked on MTEB Chinese benchmarks). Switching only requires changing `EmbeddingModel` in `appsettings.json` and rebuilding the vector store.

---

## Known Pitfalls

- **Vector dimension binding**: `nomic-embed-text` outputs 768 dimensions, `bge-m3` outputs 1024 dimensions. The SQLite table is created with a hardcoded dimension, so **old vectors must be cleared and rebuilt after switching models** — they cannot be mixed.
- **Low Chinese recall**: `nomic-embed-text` has weak Chinese semantic understanding. Cosine similarity between Chinese synonymous sentences is noticeably lower than for English, leading to lower retrieval recall. This is a known limitation of the current system.

---

## Interview Talking Points

- DIP in AI engineering: interface-isolating LLM/Embedding providers is standard practice in enterprise AI applications
- Cost analysis of local vs. cloud embedding at scale: the savings in large-batch ingestion is not just money, but also the compounding effect of latency
- Vector dimension consistency: why switching embedding models requires rebuilding the vector store (dot product / cosine similarity are meaningless across different dimensional spaces)
