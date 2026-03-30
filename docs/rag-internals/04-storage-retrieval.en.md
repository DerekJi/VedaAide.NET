> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[04-storage-retrieval.cn.md](04-storage-retrieval.cn.md)

# 04 — Storage Layer & Vector Retrieval

> Covers both vector store implementations (SQLite / CosmosDB), cosine similarity computation, keyword retrieval, and the semantic cache mechanism.

---

## 1. Dual Storage Implementation Comparison

```plantuml
@startuml storage-dual-impl
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Dual Vector Store Implementations (IVectorStore)

interface IVectorStore {
  +UpsertAsync(chunk)
  +UpsertBatchAsync(chunks)
  +SearchAsync(queryEmbedding, topK, ...)
  +SearchByKeywordsAsync(query, topK, ...)
  +MarkDocumentSupersededAsync(name, newDocId)
  +GetCurrentChunksByDocumentNameAsync(name)
  +GetVersionHistoryAsync(name)
  +ExistsAsync(contentHash)
  +DeleteByDocumentAsync(documentId)
}

class SqliteVectorStore {
  - VedaDbContext db
  ..
  Vector storage: BLOB (float[] little-endian)
  Similarity: in-memory full-scan cosine
  Keywords: LIKE in-memory filter
  Suitable for: local dev / <100K chunks
}

class CosmosDbVectorStore {
  - Container _container
  ..
  Vector storage: JSON Array (float[])
  Similarity: DiskANN approximate nearest neighbor (ANN)
  Keywords: CONTAINS full-text match
  Suitable for: production / large scale
}

IVectorStore <|.. SqliteVectorStore
IVectorStore <|.. CosmosDbVectorStore

note bottom of SqliteVectorStore
  Switch via:
  appsettings.json
  "Veda:StorageProvider": "Sqlite"
  or "CosmosDb"

  SQLite vector search:
  Load all chunks into memory
  Batch-compute cosine similarity
  → Suitable for <100K entries
end note

note bottom of CosmosDbVectorStore
  CosmosDB vector search SQL:
  SELECT TOP @n *
  FROM c
  ORDER BY VectorDistance(
    c.embedding, @qvec, false,
    {"distanceFunction":"cosine"}
  )
  WHERE c.supersededAtTicks = 0
  
  DiskANN index:
  Approximate nearest neighbor, trades
  a small accuracy loss for O(log N) complexity
end note

@enduml
```

---

## 2. Cosine Similarity Computation

```plantuml
@startuml cosine-similarity
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 13

title Cosine Similarity (VectorMath.CosineSimilarity)

rectangle "Input" as In #E8F4FD {
  rectangle "Vector a\n(queryEmbedding)" as A
  rectangle "Vector b\n(chunk.Embedding)" as B
}

rectangle "Computation (VectorMath.cs)" as Calc #E8F8E8 {
  note as CalcNote
    dot   = Σ a[i] × b[i]         (dot product)
    normA = √(Σ a[i]²)            (L2 norm)
    normB = √(Σ b[i]²)            (L2 norm)
    
    similarity = dot / (normA × normB)
    
    Range: [-1, 1]
    · 1.0  → fully aligned (same semantics)
    · 0.0  → orthogonal (unrelated)
    · -1.0 → fully opposed (opposite semantics)
    
    Returns 0 on dimension mismatch (safe fallback)
  end note
}

rectangle "Usage Contexts" as Usage #FFF3CD {
  note as UNote
    · Vector search (SearchAsync):
      chunk similarity ≥ minSimilarity(0.3) to be returned
      
    · Semantic cache (SemanticCache):
      question similarity ≥ 0.95 counts as a hit
      
    · Hallucination guard layer 1:
      answer similarity < 0.3 flagged as hallucination
      
    · Semantic dedup (Ingest):
      chunk similarity ≥ 0.95 skipped as near-duplicate
      
    · Evaluator (AnswerRelevancyScorer):
      similarity between question and answer = relevancy score
  end note
}

In --> Calc
Calc --> Usage

@enduml
```

---

## 3. SQLite Storage Schema

```plantuml
@startuml sqlite-schema
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 11

title SQLite Storage Schema (VedaDbContext)

entity VectorChunks {
  * Id : string (PK)
  --
  DocumentId : string (INDEX)
  DocumentName : string (INDEX)
  DocumentType : int  (enum)
  Content : string
  ChunkIndex : int
  ContentHash : string (UNIQUE) — SHA-256
  EmbeddingBlob : byte[] — float[] little-endian
  EmbeddingModel : string — model version tag
  MetadataJson : string — {"wordCount":"...", "aliasTags":"..."}
  CreatedAtTicks : long
  Version : int — increments from 1
  SupersededAtTicks : long — 0=active, >0=superseded
  SupersededByDocId : string
}

entity SemanticCache {
  * Id : string (PK)
  --
  EmbeddingBlob : byte[] — question vector
  Answer : string
  CreatedAtTicks : long
  ExpiresAtTicks : long — TTL
}

entity UserBehaviors {
  * Id : string (PK)
  --
  UserId : string (INDEX)
  SessionId : string
  Type : int — Accept/Reject/View
  RelatedChunkId : string (INDEX)
  RelatedDocumentId : string
  Query : string
  OccurredAtTicks : long
}

entity SyncedFiles {
  * Id : string (PK)
  --
  ConnectorName : string (UNIQUE KEY)
  FilePath : string (UNIQUE KEY)
  ContentHash : string — SHA-256
  DocumentId : string
  SyncedAt : DateTimeOffset
}

entity PromptTemplates {
  * Id : string (PK)
  --
  Name : string (UNIQUE KEY)
  Version : string (UNIQUE KEY)
  Content : string
  CreatedAt : DateTimeOffset
}

entity SharingGroups {
  * Id : string (PK)
  --
  OwnerId : string
  MembersJson : string — JSON array
  CreatedAtTicks : long
}

entity DocumentPermissions {
  * Id : string (PK)
  --
  DocumentId : string
  GroupId : string
  GrantedAtTicks : long
}

VectorChunks ||--o{ UserBehaviors : "RelatedChunkId"
SharingGroups ||--o{ DocumentPermissions : "GroupId"

@enduml
```

---

## 4. CosmosDB Storage Schema

```plantuml
@startuml cosmosdb-schema
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title CosmosDB NoSQL Storage Schema

rectangle "Database: VedaAide" as DB #E8F4FD {

  rectangle "Container: VectorChunks\nPartition Key: /documentId" as VC #E8F8E8 {
    note as VCNote
      {
        "id": "uuid",
        "documentId": "uuid",       ← Partition Key
        "documentName": "file.md",
        "documentType": 0,
        "content": "...",
        "chunkIndex": 0,
        "contentHash": "sha256hex",
        "embedding": [0.1, -0.2, ...],  ← vector field (1536 dims)
        "embeddingModel": "text-embedding-3-small",
        "metadataJson": "{...}",
        "createdAtTicks": 123456789,
        "version": 1,
        "supersededAtTicks": 0,         ← 0=active
        "supersededByDocId": ""
      }
      
      Vector index policy (DiskANN):
      {
        "path": "/embedding",
        "type": "diskann",
        "distanceFunction": "cosine",
        "dimensions": 1536
      }
    end note
  }

  rectangle "Container: SemanticCache\nPartition Key: /id" as SC #FFF3CD {
    note as SCNote
      {
        "id": "uuid",
        "embedding": [0.1, ...],
        "answer": "answer text",
        "createdAt": 1234567890,  ← Unix epoch
        "expiresAt": 1234571490  ← TTL
      }
      
      Currently matched via in-memory cosine
      (DiskANN vector index reserved for future optimization)
    end note
  }
}

note right of DB
  Authentication:
  · AccountKey (explicit key)
  · DefaultAzureCredential
    (Managed Identity / az login)
  
  Switch via:
  "Veda:StorageProvider": "CosmosDb"
  "Veda:CosmosDb:Endpoint": "..."
end note

@enduml
```

---

## 5. Vector Search Comparison: SQLite vs CosmosDB

```plantuml
@startuml vector-search-compare
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Vector Search: SQLite vs CosmosDB

rectangle "SQLite Vector Search\n(SearchAsync)" as SL #E8F4FD {
  note as SLNote
    1. Load all valid rows from VectorChunks
       (SupersededAtTicks == 0)
       + optional filters: DocumentType / DateRange

    2. For each row:
       embedding = BlobToFloats(EmbeddingBlob)
       similarity = CosineSimilarity(queryEmbedding, embedding)

    3. Filter similarity >= minSimilarity
    4. Filter KnowledgeScope (in-memory)
    5. Sort by similarity descending
    6. Take(topK)

    Characteristics:
    · Exact nearest neighbor (full scan)
    · Memory footprint = rows × dimensions × 4 bytes
    · Suitable for <100K entries (~<400MB RAM)
  end note
}

rectangle "CosmosDB Vector Search\n(SearchAsync)" as CDB #E8F8E8 {
  note as CDBNote
    SQL template:
    SELECT TOP @candidateCount *
    FROM c
    ORDER BY VectorDistance(
      c.embedding, @vecJson, false,
      {"distanceFunction":"cosine","dataType":"float32"}
    )
    WHERE c.supersededAtTicks = 0
      [AND c.documentType = @type]
      [AND c.createdAtTicks >= @dateFrom]

    Post-processing (in-memory):
    · Filter similarity >= minSimilarity
    · Filter KnowledgeScope
    · Take(topK)

    Characteristics:
    · Approximate nearest neighbor (DiskANN index)
    · candidateCount = topK × 4 (compensates for ANN loss)
    · Millisecond response at million-document scale
  end note
}

@enduml
```

---

## 6. Semantic Cache Operation

```plantuml
@startuml semantic-cache
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Semantic Cache (ISemanticCache)

participant "QueryService" as Q
participant "ISemanticCache" as Cache
participant "Vector Store" as VS
participant "LLM" as LLM

Q -> Q : Generate queryEmbedding

Q -> Cache : GetAsync(queryEmbedding)

alt Cache hit (similarity ≥ 0.95 and not expired)
  Cache --> Q : cachedAnswer
  Q --> Q : Return directly, skip retrieval and LLM
  note right of Cache
    Hit condition:
    · Load all non-expired entries
    · For each entry:
      CosineSimilarity(queryEmb, entryEmb) ≥ 0.95
    · Return first matching answer
  end note
else Cache miss
  Cache --> Q : null
  Q -> VS : Hybrid retrieval
  Q -> LLM : Generate answer
  
  alt !IsHallucination
    Q -> Cache : SetAsync(queryEmbedding, answer, ttl)
    note right of Cache
      TTL default: 1 hour
      Cleared on knowledge base update:
      DocumentIngestService.IngestAsync
      → SemanticCache.ClearAsync()
    end note
  end
end

@enduml
```
