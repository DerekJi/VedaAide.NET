> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[02-ingest-flow.cn.md](02-ingest-flow.cn.md)

# 02 — Ingest Pipeline

> The complete journey of a document (text or file) into the VedaAide knowledge base.

---

### Semantic Enhancement (Detailed Explanation)

**Core Concept**: During ingestion, use a personal vocabulary configuration to automatically detect terms in chunks, then replace them in-place with "term (synonym1 synonym2)" format to enhance embedding semantic associations.

**Example Walkthrough**:

| Stage | Content |
|-------|---------|
| **Original chunk** | The BG is too dark, so James had to be very careful. |
| **Vocabulary** | term="BG", synonyms=["背景资料", "context"] |
| **After Enhancement** | The BG (背景资料 context) is too dark, so James had to be very careful. |
| **Stored Metadata** | aliasTags: [], detectedTerms: {"BG": ["背景资料", "context"]} |
| **For Embedding** | Full enriched text; embedding captures semantic associations between BG/背景资料/context |

**Advantages**:
- ✅ Syntax coherent, high-quality embedding semantics
- ✅ User queries with "background" or "背景资料" will retrieve this chunk (via embedding similarity)
- ✅ Original matched case is preserved (e.g., "BG" stays "BG", not "bg")
- ✅ Avoids double-replacement (already-replaced terms are not replaced again)

---

## 1. Overall Ingest Flow

```plantuml
@startuml ingest-overview
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam activityBackgroundColor #E8F8E8
skinparam activityBorderColor #4CAF50

title VedaAide — Ingest Pipeline Overview

|User / Data Source|
start
:Submit content;
note right
  Three entry points:
  POST /api/documents        (plain text)
  POST /api/documents/upload (PDF/image)
  DataSourceConnector auto-sync
end note

|DocumentsController / Connector|
:Determine content type;
if (Is file stream?) then (yes)
  :Route to IFileExtractor;
  if (DocumentType == RichMedia?) then (yes)
    |VisionModelFileExtractor|
    :GPT-4o-mini Vision\nextract text/image content;
  else (no)
    |DocumentIntelligenceFileExtractor|
    :Azure Document Intelligence\nOCR + layout-aware extraction;
    note right
      BillInvoice → prebuilt-invoice
      Others      → prebuilt-read
    end note
  endif
  :Obtain plain text (extractedText);
else (no — plain text goes directly)
endif

|DocumentIngestService|
:Check if document with same name exists\n(vectorStore.GetCurrentChunksByDocumentNameAsync);
if (Existing document found?) then (yes)
  :DocumentDiffService.DiffAsync\nCompare old and new content, generate change summary\n(word-set diff + LLM-extracted change topics);
  :Compute new version = oldMax + 1;
else (no — first ingestion)
  :version = 1;
endif
:Generate new documentId (Guid);

|TextDocumentProcessor|
:Select ChunkingOptions by DocumentType\n(TokenSize / OverlapTokens);
note right
  BillInvoice   256 / 32 tokens
  PersonalNote  256 / 32 tokens
  Report        512 / 64 tokens
  RichMedia     512 / 64 tokens
  Specification 1024 / 128 tokens
end note
:Sliding-window chunking\n1 word ≈ 1.3 tokens (conservative estimate);
:Return List<DocumentChunk>;

|DocumentIngestService|
:Semantic enhancement — SemanticEnhancer.GetEnhancedMetadataAsync\nExtract alias tags and detected terms for each chunk, write to Metadata: aliasTags, detectedTerms;

|EmbeddingService|
:Batch call IEmbeddingGenerator\n(Azure OpenAI text-embedding-3-small\nor Ollama bge-m3);
:Return float[][] embeddings;

|DocumentIngestService|
:Write embedding into chunk.Embedding;
:Record chunk.EmbeddingModel (used for re-indexing on model switch);

:Second-layer dedup (semantic near-duplicate filter)\nFor each chunk, query vector store with its embedding\nSimilarity ≥ SimilarityDedupThreshold(0.95) → skip;
note right
  Layer 1 dedup: SHA-256 content hash (storage layer)
  Layer 2 dedup: embedding cosine similarity ≥ 0.95
end note

if (Old version exists?) then (yes)
  :MarkDocumentSupersededAsync\nMark old chunks as superseded\n(SupersededAtTicks = now);
  note right
    Mark first, then write!
    Prevents new chunks from being
    immediately superseded.
  end note
endif

|IVectorStore (SQLite or CosmosDB)|
:UpsertBatchAsync\nBatch-write deduplicated new chunks;
note right
  Storage-layer first dedup:
  Pre-compute SHA-256, batch-query existing hashes
  Avoids N+1 problem
end note

|DocumentIngestService|
:SemanticCache.ClearAsync\nClear semantic cache (async, non-blocking);
note right
  Knowledge base content changed —
  cached answers may be stale.
end note

:Return IngestResult\n(documentId, chunksStored);

|User / Data Source|
:Receive ingest result;
stop

@enduml
```

---

## 2. File Extraction Routing

```plantuml
@startuml ingest-file-routing
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title File Extraction Routing (IngestFileAsync)

participant "DocumentsController" as Ctrl
participant "DocumentIngestService" as Svc
participant "DocumentIntelligenceFileExtractor" as DI
participant "VisionModelFileExtractor" as Vision
participant "Azure Document Intelligence" as ADI
participant "Azure OpenAI\n(GPT-4o-mini Vision)" as AOI

Ctrl -> Svc : IngestFileAsync(stream, fileName, mimeType, documentType)

alt documentType == RichMedia
  Svc -> Vision : ExtractAsync(stream, fileName, mimeType, documentType)
  Vision -> AOI : ChatHistory with ImageContent\n+ extraction prompt
  AOI --> Vision : text/symbol/handwriting description
  Vision --> Svc : extractedText
else Other types (BillInvoice / Report / Specification / Other)
  Svc -> DI : ExtractAsync(stream, fileName, mimeType, documentType)
  alt documentType == BillInvoice
    DI -> ADI : AnalyzeDocumentAsync("prebuilt-invoice", stream)
  else
    DI -> ADI : AnalyzeDocumentAsync("prebuilt-read", stream)
  end
  ADI --> DI : AnalyzeResult.Content\n(full text in Markdown format)
  DI --> Svc : extractedText
end

Svc -> Svc : IngestAsync(extractedText, fileName, documentType)
note right: Continues to standard text ingest flow

@enduml
```

---

## 3. Chunking Strategy

```plantuml
@startuml chunking
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Sliding-Window Chunking Strategy (TextDocumentProcessor)

rectangle "Source text (words[])" as Text #E8F4FD

rectangle "Chunk 0\n[0 .. wordsPerChunk)" as C0 #E8F8E8
rectangle "Chunk 1\n[wordsPerChunk - overlapWords .. 2×wordsPerChunk - overlapWords)" as C1 #E8F8E8
rectangle "Chunk 2\n..." as C2 #E8F8E8

Text --> C0
Text --> C1
Text --> C2

note bottom of C0
  wordsPerChunk = TokenSize / 1.3
  overlapWords  = OverlapTokens / 1.3

  Slide step = wordsPerChunk - overlapWords
  — Adjacent chunks share overlapWords words
  — Preserves semantic continuity
end note

rectangle "ChunkingOptions by DocumentType" as Opts #FFF3CD
note right of Opts
  | DocumentType   | TokenSize | Overlap |
  |----------------|-----------|---------|
  | BillInvoice    | 256       | 32      |
  | PersonalNote   | 256       | 32      |
  | Report         | 512       | 64      |
  | RichMedia      | 512       | 64      |
  | Specification  | 1024      | 128     |
  | Other (default)| 512       | 64      |
end note

@enduml
```

---

## 4. Versioning & Deduplication

```plantuml
@startuml versioning-dedup
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Document Versioning + Two-Layer Deduplication

rectangle "Ingest Phase" as Ingest {
  rectangle "Layer 1 Dedup\n(Storage — content hash)" as L1 #E8F4FD
  rectangle "Layer 2 Dedup\n(Service — semantic similarity)" as L2 #E8F8E8
  rectangle "Versioning\n(MarkSuperseded)" as V #FFF3CD
}

note right of L1
  Compute SHA-256(content)
  Batch-query DB for existing hashes
  Identical content → skip
  (exact dedup)
end note

note right of L2
  Query vector store with chunk.embedding
  TopK=1, minSimilarity=0.95
  Semantically near-identical → skip
  (fuzzy dedup, prevents synonym duplicates)
end note

note right of V
  When same-name document is re-ingested:
  1. MarkDocumentSupersededAsync first
     (oldChunks.SupersededAtTicks = now)
  2. Then UpsertBatchAsync to write new chunks

  Query filter: WHERE SupersededAtTicks == 0
  — Only retrieves currently valid chunks
end note

@enduml
```

---

## 5. Data Source Auto-Sync

```plantuml
@startuml datasource-sync
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Data Source Auto-Sync Flow

participant "DataSourceSyncBackgroundService" as BG #E8F4FD
participant "FileSystemConnector / BlobStorageConnector" as Conn #E8F8E8
participant "ISyncStateStore (SQLite)" as State #F8E8FF
participant "IDocumentIngestor" as Ingest #FFF3CD

BG -> BG : Delay 30s (wait for API startup)
loop Every IntervalMinutes
  BG -> Conn : SyncAsync()
  loop Each file / Blob
    Conn -> State : GetContentHashAsync(connector, filePath)
    State --> Conn : Last hash (or null)
    Conn -> Conn : Compute current SHA-256
    alt Same hash — content unchanged
      Conn -> Conn : Skip (filesSkipped++)
    else Different hash / new file
      alt Text file (.txt/.md)
        Conn -> Ingest : IngestAsync(content, name, docType)
      else Binary file (PDF/image)
        Conn -> Ingest : IngestFileAsync(stream, name, mimeType, docType)
      end
      Conn -> State : UpsertAsync(connector, filePath, newHash, documentId)
    end
  end
  Conn --> BG : DataSourceSyncResult\n(filesProcessed, chunksStored, errors)
end

note right of BG
  Veda:DataSources:AutoSync:
    Enabled: true
    IntervalMinutes: 60
  
  Each Sync creates an isolated DI Scope
  to ensure DbContext instance isolation
end note

@enduml
```
