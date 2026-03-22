# Phase 2 RAG Quality Enhancement Test Plan

> 中文版见 [phase2-rag-tests.cn.md](phase2-rag-tests.cn.md)

## 1. Test Scope

Phase 2 adds the following quality enhancement components on top of Phase 1:

| Component | Test Type | Test File |
|---|---|---|
| `DocumentIngestService` (similarity dedup) | Unit (Mock) | `Veda.Services.Tests/DocumentIngestServiceTests.cs` |
| `QueryService` (reranking + hallucination layer 1) | Unit (Mock) | `Veda.Services.Tests/QueryServiceTests.cs` |
| `HallucinationGuardService` (hallucination layer 2) | Unit (Mock) | `Veda.Services.Tests/HallucinationGuardServiceTests.cs` |
| `SqliteVectorStore` (date range filter) | Integration (temp SQLite) | `Veda.Services.Tests/SqliteVectorStoreIntegrationTests.cs` |
| `POST /api/documents` (dedup behavior) | Smoke | `scripts/smoke-test.sh` |
| `POST /api/query` (hallucination fields) | Smoke | `scripts/smoke-test.sh` |
| `POST /api/query` (date filter) | Smoke | `scripts/smoke-test.sh` |


## 2. Feature Descriptions and Test Goals

### 2.1 Vector Similarity Deduplication (Ingestion Phase)

**Location:** `DocumentIngestService.IngestAsync`, after embedding, before `UpsertBatchAsync`.

**Core logic:** For each new chunk, search the vector store for the nearest neighbor (TopK=1). If similarity ≥ `RagOptions.SimilarityDedupThreshold` (default 0.95), skip the chunk — it is a semantic duplicate.

**Test goals:**

| Scenario | Expected Behavior |
|---|---|
| New content, empty store or no similar chunks | All chunks stored, `ChunksStored` equals total chunk count |
| One chunk has similarity ≥ threshold with stored content | That chunk is skipped, `ChunksStored` decremented by 1 |
| All chunks are near-duplicates | `UpsertBatchAsync` not called, `ChunksStored = 0` |
| Threshold set to `1.1f` (never triggered) | All chunks stored normally — degrades to Phase 1 behavior |

**Configurability check:** Set `Veda:Rag:SimilarityDedupThreshold` to `0.0` in `appsettings.json`, then re-ingest the same document. All chunks should be skipped (self-cosine similarity = 1.0).


### 2.2 Hallucination Detection Layer 1 (Answer Embedding Check)

**Location:** `QueryService.QueryAsync`, after LLM generates the answer.

**Core logic:** Embed the full answer text, then search the vector store (TopK=1). If the highest similarity < `RagOptions.HallucinationSimilarityThreshold` (default 0.3), set `RagQueryResponse.IsHallucination = true`.

**Test goals:**

| Scenario | Expected Behavior |
|---|---|
| Answer embedding has high similarity to document store (≥ 0.3) | `IsHallucination = false` |
| Answer embedding has very low similarity (< 0.3) | `IsHallucination = true` |
| Document store is empty (no matches) | Max similarity = 0, `IsHallucination = true` |


### 2.3 Hallucination Detection Layer 2 (LLM Self-Check)

**Location:** `HallucinationGuardService.VerifyAsync`, called by `QueryService` after layer 1 passes and when `EnableSelfCheckGuard = true`.

**Core logic:** Send a strict fact-checking prompt to the LLM along with the original context and generated answer; the LLM must return `true` or `false`. Only responses starting with `true` (case-insensitive) are considered grounded.

**Test goals:**

| Scenario | Expected Behavior |
|---|---|
| LLM returns `"true"` | `VerifyAsync` returns `true` (answer is grounded) |
| LLM returns `"false"` | `VerifyAsync` returns `false` (potential hallucination) |
| LLM returns `" True\n"` (with whitespace) | Correctly handled, returns `true` |
| `EnableSelfCheckGuard = false` | Layer 2 skipped entirely, `VerifyAsync` not called |
| Layer 1 already flagged hallucination | Layer 2 not called (short-circuit logic) |


### 2.4 Reranking (Re-sorting Retrieval Results)

**Location:** Private method `Rerank` inside `QueryService.QueryAsync`.

**Core logic:** Initially retrieve `TopK × RerankCandidatesMultiplier` (default 2×) candidates, then re-score by 70% vector similarity + 30% keyword coverage, and take the top `TopK`.

**Test goals:**

| Scenario | Expected Behavior |
|---|---|
| Multiple candidates, some containing question keywords | Chunks with more keyword matches ranked higher |
| `TopK=3` but candidates exceed 3 | Final `Sources` count = 3 |
| No candidates contain any question keywords | Degrades to pure vector similarity ranking |


### 2.5 Date Range Filter (Vector Retrieval Phase)

**Location:** `SqliteVectorStore.SearchAsync` (`WHERE CreatedAtTicks >= dateFrom` / `<= dateTo`);
passed in via `RagQueryRequest.DateFrom` / `DateTo`, received at the API layer via `QueryRequest.DateFrom` / `DateTo`.

**Test goals:**

| Scenario | Expected Behavior |
|---|---|
| `DateFrom` set to a point in time, store has chunks before and after | Only chunks at or after `DateFrom` are returned |
| `DateTo` set to a point in time | Only chunks at or before `DateTo` are returned |
| Both `DateFrom` and `DateTo` set | Only chunks within the time window are returned |
| `DateFrom` / `DateTo` not set (null) | No date filtering, all otherwise matching chunks returned |


## 3. Smoke Test Plan

Extending Phase 1 smoke tests with Phase 2 specific validations:

| Test Point | Action | What is Verified |
|---|---|---|
| Dedup behavior | Submit same content twice to `POST /api/documents` | Second call returns `chunksStored = 0` |
| Hallucination field | Normal `POST /api/query` | Response JSON contains `isHallucination` field (`true` or `false`) |
| Date filter | `POST /api/query` with `dateFrom` set to far future (e.g., `"2099-01-01T00:00:00Z"`) | `sources` list is empty, `answer` returns "not enough information" |
| Date filter | `POST /api/query` without `dateFrom`/`dateTo` | Behaves same as Phase 1, normal results returned |
