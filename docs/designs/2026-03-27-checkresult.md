# Stage 1/2/3 Implementation Completeness Check

## 1. Stage 1/2/3 Implementation Completeness

**Compiles without errors; overall completion is high.** Verified item by item against the design docs:

### Implemented ✅
| Module | Status |
|---|---|
| Stage 1: RAG foundation, anti-hallucination, Agent, MCP Server, evaluation system, GraphQL | ✅ |
| Stage 2: CosmosDB switch, LLM routing (simple/advanced), semantic cache, API Key auth, CORS, Rate Limiting, Admin tools | ✅ |
| Stage 3 Sprint1: HybridRetriever, KnowledgeScope filtering, SearchByKeywordsAsync | ✅ |
| Stage 3 Sprint2: DocumentIntelligence extraction, VisionModel extraction, file upload endpoints | ✅ |
| Stage 3 Sprint3: StructuredFinding, StructuredOutputParser, DocumentDiffService, versioned fields, SemanticEnhancer | ✅ |
| Stage 3 Sprint4: UserBehaviorEvent, UserMemoryStore, FeedbackBoostService, GovernanceController, privacy isolation | ✅ |

### Not yet implemented ⚠️ (consistent with the leftover items explicitly marked in the design docs)
| Item | Notes |
|---|---|
| `AdminController.Stats` lacks cache hit-rate statistics | Sprint3 leftover |
| `DocumentIngestService` does not invalidate the cache after ingest | Sprint3 leftover |
| `/mcp` endpoint not protected by API Key | Sprint2 leftover security risk |
| Ingestion completeness evaluation metrics (`Veda.Evaluation` integration) | Sprint2 leftover |

---

## 2. Code Logic Inconsistencies and Bugs

### 🐛 Critical Bug: `MarkDocumentSupersededAsync` also marks new chunks as superseded

**Location**: `DocumentIngestService.IngestAsync`; both the SQLite and CosmosDB implementations are affected.

**Cause**: the call order is:
1. `UpsertBatchAsync(deduped)` → new chunks written, `SupersededAtTicks == 0`
2. `MarkDocumentSupersededAsync(documentName, newDocumentId)` → WHERE condition is `DocumentName == name AND SupersededAtTicks == 0`

Step 2's WHERE matches both the old chunks and the just-inserted new chunks, so the new chunks are immediately marked superseded (by themselves). After that, every query (`WHERE SupersededAtTicks == 0`) can no longer find the new document content.

**There is an additional CosmosDB bug**: `MarkDocumentSupersededAsync`'s Patch operation uses `PartitionKey.None`, but the container's PartitionKey is `/documentId`. Patch requires the exact PartitionKey; cross-partition write/update operations are not supported in CosmosDB and will throw an exception.

### 🐛 Bug: `QueryStreamAsync` behaves inconsistently with `QueryAsync`
The streaming query has three omissions (the non-streaming path implements all of them correctly):

| Feature | QueryAsync | QueryStreamAsync |
|---|---|---|
| HybridRetriever (dual-channel retrieval) | ✅ Used per configuration | ❌ Always calls `vectorStore.SearchAsync` directly |
| KnowledgeScope filtering | ✅ Passes `scope: request.Scope` | ❌ Missing the `scope` parameter; completely ignored |
| FeedbackBoost personalized ranking | ✅ Applied by userId | ❌ Does not call `feedbackBoostService` |

### ⚠️ Potential concurrency bug: HybridRetriever operates on the same DbContext concurrently

```csharp
// HybridRetriever.cs
var vectorTask = vectorStore.SearchAsync(...);    // starts the first query
var keywordTask = vectorStore.SearchByKeywordsAsync(...)  // immediately starts the second query
await Task.WhenAll(vectorTask, keywordTask);      // waits concurrently
```

Both tasks use the same `SqliteVectorStore` instance (Scoped), and therefore the same `VedaDbContext`. EF Core's `DbContext` does not support concurrent operations and may throw `InvalidOperationException: A second operation was started on this context instance before a previous asynchronous operation completed`. This may surface intermittently on SQLite; the CosmosDB path is unaffected (the CosmosDB implementation does not depend on DbContext).

### ⚠️ Security concern: `IsDocumentVisibleToUserAsync` matches userId with string Contains

```csharp
var groups = await db.SharingGroups
    .Where(g => g.MembersJson.Contains(userId))  // potential substring false match
```

If userId=`user1` and MembersJson=`["user12","user13"]`, Contains matches incorrectly. It should use proper JSON parsing or add quote-boundary matching (`Contains($"\"{userId}\"")`).

---

## 3. Migration Handling When Switching Databases

**An automatic migration mechanism already exists, but it only covers SQLite.**

### Existing Mechanism

At startup, `Program.cs` runs:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VedaDbContext>();
    await db.Database.MigrateAsync();  // applies all pending migrations automatically
}
```

If switched to CosmosDB, it additionally initializes asynchronously:
```csharp
var cosmosInitializer = app.Services.GetService<CosmosDbInitializer>();
if (cosmosInitializer is not null)
    await cosmosInitializer.EnsureReadyAsync(initCts.Token);  // creates Database/Container/Vector Index
```

### Switch Scenario Analysis

| Scenario | Behavior |
|---|---|
| **SQLite→SQLite** (first start or new schema fields) | ✅ `MigrateAsync()` applies automatically, safe |
| **CosmosDB→CosmosDB** (first start) | ✅ `CosmosDbInitializer` creates containers and vector index automatically (idempotent) |
| **SQLite→CosmosDB** (change `Veda:StorageProvider`) | ⚠️ Old SQLite data is **not migrated** to CosmosDB; the knowledge base must be re-ingested; `MigrateAsync()` still runs (the SQLite metadata DB exists independently), vector data starts from zero |
| **CosmosDB→SQLite** | ⚠️ Same as above: vector data in CosmosDB is not synced to SQLite; documents must be re-ingested |
| **Embedding model change (dimension change)** | ❌ **No automatic detection**; old embedding dimensions are incompatible with the new model; you must manually `DELETE /api/admin/data` to clear and then re-ingest |

**Conclusion**: switching StorageProvider does not error or crash (`MigrateAsync` only operates on the SQLite metadata DB; each vector store follows its own path), but **vector knowledge-base data is not migrated automatically** — you must re-trigger data-source sync after switching. The design doc already states this behavior explicitly ("change config + clear data + re-ingest").

---

## Recommended Fix Priorities

| Priority | Issue |
|---|---|
| P0 (data corruption) | `MarkDocumentSupersededAsync` wrongly marks new chunks: mark old chunks BEFORE `UpsertBatch`, or add `DocumentId != newDocumentId` to the WHERE |
| P0 (CosmosDB) | `PatchItemAsync` uses `PartitionKey.None`; change it to use the `documentId` from the query result as the PartitionKey |
| P1 (behavior inconsistency) | `QueryStreamAsync` should add: HybridRetriever, KnowledgeScope, FeedbackBoost |
| P1 (concurrency safety) | `HybridRetriever` should await sequentially, or use a separate DbContext scope |
| P2 (security) | `SharingGroups.MembersJson.Contains(userId)` → exact JSON matching |
| P2 (leftover gaps) | cache invalidation, stats cache hit-rate, `/mcp` authentication |
