# Dual-Layer Deduplication: Hash Dedup + Vector Similarity Dedup

**Project**: VedaAide.NET  
**Files**: `src/Veda.Services/DocumentIngestService.cs`, `src/Veda.Storage/SqliteVectorStore.cs`  
**Phase**: Phase 2

> 中文版见 [dedup-dual-layer.cn.md](dedup-dual-layer.cn.md)

---

## Problem Background

Users repeatedly uploading the same or similar documents is a common scenario (version iterations, re-imports). Without deduplication:
- The vector store accumulates multiple copies of the same content, which get repeatedly hit during retrieval, producing redundant citations in answers
- Similarity distributions are skewed upward, affecting hallucination detection thresholds
- Storage grows unboundedly

---

## Why Two Layers

### Layer 1: Hash Deduplication (Exact Matches)

Compute SHA-256 of each chunk's text content and store it. Before ingesting a new chunk, check the hash — if it matches, skip immediately.

**Characteristics**: O(1) lookup, zero extra cost, but only identifies **perfectly identical** content.

**Limitation**: A single punctuation difference changes the hash entirely — it cannot detect "essentially the same" content.

### Layer 2: Vector Similarity Deduplication (Semantic Near-Duplicates)

After generating embeddings and before `UpsertBatchAsync`, search the vector store for each new chunk:

```csharp
// DocumentIngestService.cs
var similar = await vectorStore.SearchAsync(
    chunk.Embedding!, topK: 1, minSimilarity: dedupThreshold, ct: ct);
if (similar.Count == 0)
    deduped.Add(chunk);  // no near-duplicate, keep it
// otherwise skip and log
```

`dedupThreshold` defaults to `0.95` (configurable in `appsettings.json`): cosine similarity ≥ 0.95 is considered a semantic duplicate.

**Characteristics**: Identifies content that has been rephrased with a few words changed — true semantic-level deduplication.

---

## How the Two Layers Complement Each Other

```
New chunk arrives
    │
    ├─ Hash match? → Skip (exact duplicate, O(1), no Embedding cost)
    │
    └─ Hash miss → Generate Embedding → Vector search
                        │
                        ├─ Similarity ≥ 0.95? → Skip (semantic duplicate)
                        └─ Similarity < 0.95  → Store in vector store
```

The hash layer is a "cheap pre-filter" for the vector layer — identical content doesn't waste an Embedding call.

---

## Threshold Selection Trade-offs

`0.95` is a conservative value meaning "only filter almost identical content":

- Threshold too high (e.g., `0.99`): Only filters copy-paste content, near-duplicates still get stored
- Threshold too low (e.g., `0.80`): Different pieces of information on the same topic are incorrectly flagged as duplicates, causing knowledge loss

For a personal assistant scenario, `0.95` is a reasonable lower bound. For highly standardized document corpora (e.g., legal statutes), lowering to `0.90` is appropriate.

---

## Interview Talking Points

- Why RAG systems need deduplication: how duplicate content interferes with retrieval ranking and hallucination detection
- Cost comparison of hash vs. vector dedup: the cost of one Embedding call vs. one hash lookup
- The business meaning of the dedup threshold: not a technical parameter, but a business decision about "how similar counts as a duplicate"
- The two-layer architecture pattern: cheap guard first, expensive operation second — a general-purpose performance optimization pattern
