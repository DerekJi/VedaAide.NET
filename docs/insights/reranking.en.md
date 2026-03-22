# Lightweight Reranking: Why Retrieve 2×TopK Then Re-sort

**Project**: VedaAide.NET  
**Files**: `src/Veda.Services/QueryService.cs` → `Rerank()`, `src/Veda.Core/RagDefaults.cs`  
**Phase**: Phase 2

> 中文版见 [reranking.cn.md](reranking.cn.md)

---

## Background: The Blind Spot of Vector Retrieval

The most basic RAG implementation is: embed the question → retrieve Top5 from the vector store → feed to LLM.

This has a fundamental problem: **vector similarity measures semantic closeness, not literal relevance**.

For example: a query like "how much is the water bill?" might have the vector-closest chunks being about "electricity bill" or "gas bill" — semantically all bills, but not the most relevant content. The truly most useful chunk might be at position 6 with just 0.01 less similarity, cut off by the Top5 threshold.

---

## Solution: Wide Retrieval First, Then Re-sort

```
Retrieve TopK × 2 = 10 candidates (wider net, less likely to miss key content)
    ↓
Lightweight re-scoring: 70% vector similarity + 30% keyword coverage
    ↓
Take top 5 after re-ranking as final context
```

**Keyword coverage** scoring logic:

```csharp
// QueryService.cs
var questionWords = question.Split(' ').Select(w => w.ToLowerInvariant()).ToHashSet();
var overlapScore = (float)contentWords.Count(w => questionWords.Contains(w)) / questionWords.Count;
var combined = 0.7f * vectorSimilarity + 0.3f * overlapScore;
```

Words from the question that also appear in a chunk get a score boost — this pulls up chunks that are lexically exact matches even if their vector score is slightly lower.

---

## Why the 70/30 Weight Split

- Vector similarity captures **semantics** (synonyms, contextual association)
- Keyword coverage captures **lexical precision** (numbers, proper nouns, item names in invoices)

Pure vectors: easily polluted by "related topic" content  
Pure keywords (BM25): cannot recognize synonyms, poor Chinese tokenization  
**Hybrid**: takes the strengths of both

70/30 is the industry starting point for hybrid search, adjustable based on the specific dataset.

---

## Limitations and Upgrade Path

The current keyword coverage is based on **whitespace tokenization**, which is nearly useless for Chinese (Chinese words have no spaces between them).

```csharp
question.Split(' ', StringSplitOptions.RemoveEmptyEntries)  // Chinese becomes one token for the whole sentence
```

This is a known limitation. Phase 4 upgrade paths:
1. **Short-term**: Integrate `jieba.NET` or similar Chinese tokenization library
2. **Medium-term**: Replace simple keyword coverage with BM25
3. **Long-term**: Use a cross-encoder model for fine-grained re-ranking (e.g., `bge-reranker`), completely replacing the current lightweight strategy

The interface is already pre-designed (currently a private method `Rerank()` inside `QueryService`). Phase 4 can extract it as `IRerankService` for dependency injection.

---

## Date-Range Metadata Filtering

Used alongside Reranking: `RagQueryRequest` supports `DateFrom`/`DateTo` parameters that filter the vector retrieval `WHERE` clause by `CreatedAtTicks`.

```sql
-- SqliteVectorStore internal query
WHERE created_at_ticks >= @dateFrom AND created_at_ticks <= @dateTo
```
