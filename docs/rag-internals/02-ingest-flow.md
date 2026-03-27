# 02 — Ingest 数据流

> 一个文档（文本或文件）如何进入 VedaAide 知识库的完整过程。

---

## 1. 整体流程图

```plantuml
@startuml ingest-overview
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam activityBackgroundColor #E8F8E8
skinparam activityBorderColor #4CAF50

title VedaAide — Ingest 整体流程

|用户 / 数据源|
start
:提交内容;
note right
  三种入口：
  POST /api/documents        (纯文本)
  POST /api/documents/upload (PDF/图片)
  DataSourceConnector 自动同步
end note

|DocumentsController / Connector|
:判断内容类型;
if (是文件流？) then (是)
  :路由到 IFileExtractor;
  if (DocumentType == RichMedia？) then (是)
    |VisionModelFileExtractor|
    :GPT-4o-mini Vision\n提取图文内容;
  else (否)
    |DocumentIntelligenceFileExtractor|
    :Azure Document Intelligence\nOCR + 版式感知提取;
    note right
      BillInvoice → prebuilt-invoice
      其他       → prebuilt-read
    end note
  endif
  :得到纯文本 extractedText;
else (否，纯文本直接进入)
endif

|DocumentIngestService|
:检查同名文档是否已存在\n(vectorStore.GetCurrentChunksByDocumentNameAsync);
if (已存在旧文档？) then (是)
  :DocumentDiffService.DiffAsync\n对比新旧内容，生成变更摘要\n(词集合差异 + LLM 提取变更主题);
  :计算新版本号 version = oldMax + 1;
else (否，首次摄取)
  :version = 1;
endif
:生成新 documentId (Guid);

|TextDocumentProcessor|
:按 DocumentType 选择 ChunkingOptions\n(TokenSize / OverlapTokens);
note right
  BillInvoice   256 / 32 tokens
  PersonalNote  256 / 32 tokens
  Report        512 / 64 tokens
  RichMedia     512 / 64 tokens
  Specification 1024 / 128 tokens
end note
:滑动窗口分块\n1 word ≈ 1.3 tokens (保守估算);
:返回 List<DocumentChunk>;

|DocumentIngestService|
:语义增强 — SemanticEnhancer.GetAliasTagsAsync\n为每个 chunk 追加别名标签到 Metadata;

|EmbeddingService|
:批量调用 IEmbeddingGenerator\n(Azure OpenAI text-embedding-3-small\n或 Ollama bge-m3);
:返回 float[][] embeddings;

|DocumentIngestService|
:将 embedding 写入 chunk.Embedding;
:记录 chunk.EmbeddingModel（模型切换时用于重索引）;

:第二层去重 (语义近似重复过滤)\n对每个 chunk 用 embedding 查向量库\n相似度 ≥ SimilarityDedupThreshold(0.95) → 跳过;
note right
  第一层去重：SHA-256 内容哈希（存储层）
  第二层去重：embedding 余弦相似度 ≥ 0.95
end note

if (旧版本存在？) then (是)
  :MarkDocumentSupersededAsync\n标记旧 chunks 为已取代\n(SupersededAtTicks = now);
  note right
    先标记后写入！
    避免新 chunk 被立刻标记为取代
  end note
endif

|IVectorStore (SQLite or CosmosDB)|
:UpsertBatchAsync\n批量写入去重后的新 chunks;
note right
  存储层第一层去重：
  预计算 SHA-256，批量查已存在哈希
  避免 N+1 问题
end note

|DocumentIngestService|
:SemanticCache.ClearAsync\n清空语义缓存（异步，不阻塞响应）;
note right
  知识库内容已变更
  旧缓存答案可能过期
end note

:返回 IngestResult\n(documentId, chunksStored);

|用户 / 数据源|
:收到摄取结果;
stop

@enduml
```

---

## 2. 文件摄取路由细图

```plantuml
@startuml ingest-file-routing
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 文件提取路由（IngestFileAsync）

participant "DocumentsController" as Ctrl
participant "DocumentIngestService" as Svc
participant "DocumentIntelligenceFileExtractor" as DI
participant "VisionModelFileExtractor" as Vision
participant "Azure Document Intelligence" as ADI
participant "Azure OpenAI\n(GPT-4o-mini Vision)" as AOI

Ctrl -> Svc : IngestFileAsync(stream, fileName, mimeType, documentType)

alt documentType == RichMedia
  Svc -> Vision : ExtractAsync(stream, fileName, mimeType, documentType)
  Vision -> AOI : ChatHistory with ImageContent\n+ 提取提示词
  AOI --> Vision : 文字/符号/手写内容描述
  Vision --> Svc : extractedText
else 其他类型（BillInvoice / Report / Specification / Other）
  Svc -> DI : ExtractAsync(stream, fileName, mimeType, documentType)
  alt documentType == BillInvoice
    DI -> ADI : AnalyzeDocumentAsync("prebuilt-invoice", stream)
  else
    DI -> ADI : AnalyzeDocumentAsync("prebuilt-read", stream)
  end
  ADI --> DI : AnalyzeResult.Content\n(Markdown 格式全文)
  DI --> Svc : extractedText
end

Svc -> Svc : IngestAsync(extractedText, fileName, documentType)
note right: 进入标准文本摄取流程

@enduml
```

---

## 3. 分块策略详解

```plantuml
@startuml chunking
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 滑动窗口分块策略（TextDocumentProcessor）

rectangle "原始文本 (words[])" as Text #E8F4FD

rectangle "Chunk 0\n[0 .. wordsPerChunk)" as C0 #E8F8E8
rectangle "Chunk 1\n[wordsPerChunk - overlapWords .. 2×wordsPerChunk - overlapWords)" as C1 #E8F8E8
rectangle "Chunk 2\n..." as C2 #E8F8E8

Text --> C0
Text --> C1
Text --> C2

note bottom of C0
  wordsPerChunk = TokenSize / 1.3
  overlapWords  = OverlapTokens / 1.3

  滑动步长 = wordsPerChunk - overlapWords
  ——相邻 chunk 共享 overlapWords 的词
  ——保持语义连续性
end note

rectangle "ChunkingOptions 按 DocumentType" as Opts #FFF3CD
note right of Opts
  | DocumentType   | TokenSize | Overlap |
  |----------------|-----------|---------|
  | BillInvoice    | 256       | 32      |
  | PersonalNote   | 256       | 32      |
  | Report         | 512       | 64      |
  | RichMedia      | 512       | 64      |
  | Specification  | 1024      | 128     |
  | Other (默认)   | 512       | 64      |
end note

@enduml
```

---

## 4. 版本控制与去重机制

```plantuml
@startuml versioning-dedup
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 文档版本化 + 双层去重

rectangle "摄取阶段" as Ingest {
  rectangle "第一层去重\n(存储层 — 内容哈希)" as L1 #E8F4FD
  rectangle "第二层去重\n(服务层 — 语义相似度)" as L2 #E8F8E8
  rectangle "版本化\n(MarkSuperseded)" as V #FFF3CD
}

note right of L1
  计算 SHA-256(content)
  批量查数据库已存在哈希
  完全相同的内容 → 跳过
  （精确去重）
end note

note right of L2
  用 chunk.embedding 查向量库
  TopK=1, minSimilarity=0.95
  语义极度相似 → 跳过
  （模糊去重，防止近义重复）
end note

note right of V
  同名文档再次摄取时：
  1. 先 MarkDocumentSupersededAsync
     (oldChunks.SupersededAtTicks = now)
  2. 再 UpsertBatchAsync 写入新 chunks

  查询时 WHERE SupersededAtTicks == 0
  ——只检索当前有效 chunks
end note

@enduml
```

---

## 5. 数据源自动同步

```plantuml
@startuml datasource-sync
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 数据源自动同步流程

participant "DataSourceSyncBackgroundService" as BG #E8F4FD
participant "FileSystemConnector / BlobStorageConnector" as Conn #E8F8E8
participant "ISyncStateStore (SQLite)" as State #F8E8FF
participant "IDocumentIngestor" as Ingest #FFF3CD

BG -> BG : 延迟 30s（等 API 启动完成）
loop 每 IntervalMinutes 分钟
  BG -> Conn : SyncAsync()
  loop 每个文件 / Blob
    Conn -> State : GetContentHashAsync(connector, filePath)
    State --> Conn : 上次哈希 (or null)
    Conn -> Conn : 计算当前 SHA-256
    alt 哈希相同 → 内容未变更
      Conn -> Conn : 跳过（filesSkipped++）
    else 哈希不同 / 新文件
      alt 文本文件 (.txt/.md)
        Conn -> Ingest : IngestAsync(content, name, docType)
      else 二进制文件 (PDF/图片)
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
  
  每次 Sync 创建独立 DI Scope
  确保 DbContext 实例隔离
end note

@enduml
```

---

## 6. 关键代码位置速查

| 步骤 | 类 / 文件 | 方法 |
|------|-----------|------|
| HTTP 入口（文本） | `DocumentsController` | `Ingest()` |
| HTTP 入口（文件） | `DocumentsController` | `Upload()` |
| MCP 入口 | `IngestTools` | `IngestDocument()` |
| 主摄取流程 | `DocumentIngestService` | `IngestAsync()` |
| 文件提取路由 | `DocumentIngestService` | `IngestFileAsync()` |
| PDF/图片 OCR | `DocumentIntelligenceFileExtractor` | `ExtractAsync()` |
| 图文 Vision 提取 | `VisionModelFileExtractor` | `ExtractAsync()` |
| 分块 | `TextDocumentProcessor` | `Process()` |
| 分块配置 | `ChunkingOptions` | `ForDocumentType()` |
| 别名注入 | `PersonalVocabularyEnhancer` | `GetAliasTagsAsync()` |
| Embedding 生成 | `EmbeddingService` | `GenerateEmbeddingsAsync()` |
| 语义近似去重 | `DocumentIngestService` | `IngestAsync()` 内循环 |
| 版本标记 | `SqliteVectorStore` / `CosmosDbVectorStore` | `MarkDocumentSupersededAsync()` |
| 批量写入 | `SqliteVectorStore` / `CosmosDbVectorStore` | `UpsertBatchAsync()` |
| 缓存清除 | `SqliteSemanticCache` / `CosmosDbSemanticCache` | `ClearAsync()` |
| 版本对比 | `DocumentDiffService` | `DiffAsync()` |
| 自动同步调度 | `DataSourceSyncBackgroundService` | `ExecuteAsync()` |
