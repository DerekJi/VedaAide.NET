> **查看图表说明：** 浏览器安装 [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) 扩展；VS Code 安装 [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) 插件。

> English version: [04-storage-retrieval.en.md](04-storage-retrieval.en.md)

# 04 — 存储层与向量检索

> 覆盖两套向量存储实现（SQLite / CosmosDB）、余弦相似度计算原理、关键词检索，以及语义缓存机制。

---

## 1. 存储层双实现对比

```plantuml
@startuml storage-dual-impl
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 向量存储双实现（IVectorStore）

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
  向量存储：BLOB (float[] little-endian)
  相似度：内存全量余弦计算
  关键词：LIKE 内存过滤
  适合：本地开发 / < 100K chunks
}

class CosmosDbVectorStore {
  - Container _container
  ..
  向量存储：JSON Array (float[])
  相似度：DiskANN 近似最近邻（ANN）
  关键词：CONTAINS 全文匹配
  适合：生产 / 大规模场景
}

IVectorStore <|.. SqliteVectorStore
IVectorStore <|.. CosmosDbVectorStore

note bottom of SqliteVectorStore
  切换方式：
  appsettings.json
  "Veda:StorageProvider": "Sqlite"
  或 "CosmosDb"

  SQLite 向量检索：
  加载全部 chunk 到内存
  批量计算余弦相似度
  → 适合 <100K 条目
end note

note bottom of CosmosDbVectorStore
  CosmosDB 向量检索 SQL：
  SELECT TOP @n *
  FROM c
  ORDER BY VectorDistance(
    c.embedding, @qvec, false,
    {"distanceFunction":"cosine"}
  )
  WHERE c.supersededAtTicks = 0
  
  DiskANN 索引：
  近似最近邻，牺牲少量精度
  换取 O(log N) 复杂度
end note

@enduml
```

---

## 2. 余弦相似度计算原理

```plantuml
@startuml cosine-similarity
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 13

title 余弦相似度（VectorMath.CosineSimilarity）

rectangle "输入" as In #E8F4FD {
  rectangle "向量 a\n(queryEmbedding)" as A
  rectangle "向量 b\n(chunk.Embedding)" as B
}

rectangle "计算过程 (VectorMath.cs)" as Calc #E8F8E8 {
  note as CalcNote
    dot  = Σ a[i] × b[i]         (点积)
    normA = √(Σ a[i]²)           (L2 范数)
    normB = √(Σ b[i]²)           (L2 范数)
    
    similarity = dot / (normA × normB)
    
    结果范围：[-1, 1]
    · 1.0  → 完全同向（语义相同）
    · 0.0  → 正交（无关）
    · -1.0 → 完全反向（语义相反）
    
    维度不一致时返回 0（安全兜底）
  end note
}

rectangle "使用场景" as Usage #FFF3CD {
  note as UNote
    · 向量检索（SearchAsync）：
      chunk 相似度 ≥ minSimilarity(0.3) 才返回
      
    · 语义缓存（SemanticCache）：
      问题相似度 ≥ 0.95 视为命中
      
    · 防幻觉第一层（HallucinationGuard）：
      答案相似度 < 0.3 视为幻觉
      
    · 语义去重（Ingest 阶段）：
      chunk 相似度 ≥ 0.95 视为近似重复跳过
      
    · 评估器（AnswerRelevancyScorer）：
      问题与答案的相似度 = 相关性分
  end note
}

In --> Calc
Calc --> Usage

@enduml
```

---

## 3. SQLite 存储结构

```plantuml
@startuml sqlite-schema
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 11

title SQLite 存储结构（VedaDbContext）

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
  EmbeddingModel : string — 模型版本标记
  MetadataJson : string — {"wordCount":"...", "aliasTags":"..."}
  CreatedAtTicks : long
  Version : int — 从 1 递增
  SupersededAtTicks : long — 0=有效, >0=已取代
  SupersededByDocId : string
}

entity SemanticCache {
  * Id : string (PK)
  --
  EmbeddingBlob : byte[] — 问题向量
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

## 4. CosmosDB 存储结构

```plantuml
@startuml cosmosdb-schema
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title CosmosDB NoSQL 存储结构

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
        "embedding": [0.1, -0.2, ...],  ← 向量字段 (1536维)
        "embeddingModel": "text-embedding-3-small",
        "metadataJson": "{...}",
        "createdAtTicks": 123456789,
        "version": 1,
        "supersededAtTicks": 0,         ← 0=有效
        "supersededByDocId": ""
      }
      
      向量索引策略 (DiskANN)：
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
        "answer": "答案文本",
        "createdAt": 1234567890,  ← Unix epoch
        "expiresAt": 1234571490  ← TTL
      }
      
      暂时通过内存余弦匹配
      （DiskANN 向量索引留待优化）
    end note
  }
}

note right of DB
  认证方式：
  · AccountKey（显式密钥）
  · DefaultAzureCredential
    （Managed Identity / az login）
  
  切换方式：
  "Veda:StorageProvider": "CosmosDb"
  "Veda:CosmosDb:Endpoint": "..."
end note

@enduml
```

---

## 5. 向量检索全流程对比

```plantuml
@startuml vector-search-compare
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 向量检索：SQLite vs CosmosDB

rectangle "SQLite 向量检索\n(SearchAsync)" as SL #E8F4FD {
  note as SLNote
    1. 从 VectorChunks 加载所有有效行
       (SupersededAtTicks == 0)
       + 可选过滤：DocumentType / DateRange

    2. 对每行：
       embedding = BlobToFloats(EmbeddingBlob)
       similarity = CosineSimilarity(queryEmbedding, embedding)

    3. 过滤 similarity >= minSimilarity
    4. 过滤 KnowledgeScope（内存）
    5. 按 similarity 降序排列
    6. Take(topK)

    特点：
    · 精确最近邻（全量扫描）
    · 内存开销 = 行数 × 维度 × 4 bytes
    · 适合 <100K 条目（约 <400MB RAM）
  end note
}

rectangle "CosmosDB 向量检索\n(SearchAsync)" as CDB #E8F8E8 {
  note as CDBNote
    SQL 模板：
    SELECT TOP @candidateCount *
    FROM c
    ORDER BY VectorDistance(
      c.embedding, @vecJson, false,
      {"distanceFunction":"cosine","dataType":"float32"}
    )
    WHERE c.supersededAtTicks = 0
      [AND c.documentType = @type]
      [AND c.createdAtTicks >= @dateFrom]

    后处理（内存）：
    · 过滤 similarity >= minSimilarity
    · 过滤 KnowledgeScope
    · Take(topK)

    特点：
    · 近似最近邻（DiskANN 索引）
    · candidateCount = topK × 4（补偿近似损失）
    · 百万级文档毫秒级响应
  end note
}

@enduml
```

---

## 6. 语义缓存工作原理

```plantuml
@startuml semantic-cache
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 语义缓存（ISemanticCache）

participant "QueryService" as Q
participant "ISemanticCache" as Cache
participant "向量库" as VS
participant "LLM" as LLM

Q -> Q : 生成 queryEmbedding

Q -> Cache : GetAsync(queryEmbedding)

alt 缓存命中（相似度 ≥ 0.95 且未过期）
  Cache --> Q : cachedAnswer
  Q --> Q : 直接返回，跳过检索和 LLM
  note right of Cache
    命中条件：
    · 加载所有未过期 entry
    · 对每条 entry：
      CosineSimilarity(queryEmb, entryEmb) ≥ 0.95
    · 返回第一条命中的 answer
  end note
else 缓存未命中
  Cache --> Q : null
  Q -> VS : 混合检索
  Q -> LLM : 生成答案
  
  alt !IsHallucination
    Q -> Cache : SetAsync(queryEmbedding, answer)
    note right of Cache
      写入条目：
      · EmbeddingBlob = queryEmbedding
      · Answer = answer
      · ExpiresAt = now + TtlSeconds(默认 3600s)
      
      知识库变更时 ClearAsync() 全量清除
    end note
  end
end

note right of Q
  配置项：
  Veda:SemanticCache:
    Enabled: false          (默认关闭)
    SimilarityThreshold: 0.95
    TtlSeconds: 3600

  优势：对语义相同但措辞不同的
  重复问题避免重复 LLM 调用
end note

@enduml
```

---

## 7. 关键参数与阈值速查

| 参数 | 默认值 | 含义 | 配置路径 |
|------|--------|------|---------|
| `minSimilarity` | 0.3 | 检索时过滤相似度下限 | `Veda:Rag:DefaultMinSimilarity` |
| `topK` | 5 | 最终返回的 chunk 数量 | 请求参数 |
| `RerankCandidatesMultiplier` | 见代码 | candidateTopK = topK × 倍数 | 代码常量 |
| `SimilarityDedupThreshold` | 0.95 | 摄取阶段语义去重阈值 | `Veda:Rag:SimilarityDedupThreshold` |
| `HallucinationSimilarityThreshold` | 0.3 | 防幻觉第一层阈值 | `Veda:Rag:HallucinationSimilarityThreshold` |
| `SemanticCache.SimilarityThreshold` | 0.95 | 缓存命中相似度阈值 | `Veda:SemanticCache:SimilarityThreshold` |
| `SemanticCache.TtlSeconds` | 3600 | 缓存 TTL（秒） | `Veda:SemanticCache:TtlSeconds` |
| `VectorWeight` | 0.7 | 混合检索向量通道权重 | `Veda:Rag:VectorWeight` |
| `KeywordWeight` | 0.3 | 混合检索关键词通道权重 | `Veda:Rag:KeywordWeight` |
| `FusionStrategy` | Rrf | 融合策略（Rrf / WeightedSum） | `Veda:Rag:FusionStrategy` |
| `EmbeddingDimensions` | 1536 | CosmosDB DiskANN 向量维度 | `Veda:CosmosDb:EmbeddingDimensions` |
| `ContextWindowBuilder.maxTokens` | 3000 | LLM 上下文 Token 预算 | 代码默认值 |
