> **查看图表说明：** 浏览器安装 [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) 扩展；VS Code 安装 [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) 插件。

> English version: [06-module-dependencies.en.md](06-module-dependencies.en.md)

# 06 — 模块依赖拓扑

> VedaAide 各 C# 项目之间的依赖关系，以及 SOLID 原则在项目边界上的体现。

---

## 1. 项目依赖拓扑图

```plantuml
@startuml module-dependencies
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam componentStyle rectangle
skinparam ArrowColor #555555

title VedaAide — 项目依赖拓扑（箭头 = 被依赖方向）

rectangle "Veda.Core\n[领域核心]" as Core #FFF8E8 {
  note as CoreNote
    · 领域模型：DocumentChunk, RagQueryRequest/Response
    · 所有接口：IVectorStore, IEmbeddingService,
      IQueryService, IDocumentIngestor, IChatService,
      IHybridRetriever, ISemanticCache, IChainOfThoughtStrategy,
      IContextWindowBuilder, IHallucinationGuardService,
      IFeedbackBoostService, IDocumentProcessor, IFileExtractor,
      ISemanticEnhancer, IDataSourceConnector ...
    · 纯数学：VectorMath.CosineSimilarity
    · 值对象：ChunkingOptions, RagDefaults, KnowledgeScope
    零外部 NuGet 依赖（除框架本身）
  end note
}

rectangle "Veda.Storage\n[基础设施层]" as Storage #F8E8FF {
  note as StorNote
    实现 IVectorStore, ISemanticCache,
    IUserMemoryStore, ISyncStateStore,
    IKnowledgeGovernanceService ...

    SqliteVectorStore / CosmosDbVectorStore
    SqliteSemanticCache / CosmosDbSemanticCache
    VedaDbContext (EF Core)
    KnowledgeGovernanceService
  end note
}

rectangle "Veda.Prompts\n[Prompt 构建]" as Prompts #E8F4FD {
  note as ProNote
    实现 IContextWindowBuilder,
    IChainOfThoughtStrategy

    ContextWindowBuilder (Token 裁剪)
    ChainOfThoughtStrategy (CoT 注入)
    无 LLM 依赖，纯字符串操作
  end note
}

rectangle "Veda.Services\n[核心服务层]" as Services #E8F8E8 {
  note as SvcNote
    实现 IDocumentIngestor, IQueryService,
    IEmbeddingService, IChatService, ILlmRouter,
    IHallucinationGuardService, IFeedbackBoostService,
    ISemanticEnhancer, IDocumentProcessor,
    IFileExtractor, IDataSourceConnector ...

    DocumentIngestService, QueryService
    HybridRetriever, LlmRouterService
    OllamaChatService, EmbeddingService
    FileSystemConnector, BlobStorageConnector
  end note
}

rectangle "Veda.Prompts" as Prompts2 #E8F4FD

rectangle "Veda.Agents\n[Agent 编排]" as Agents #FFF3CD {
  note as AgNote
    LlmOrchestrationService (IRCoT)
    OrchestrationService (手动链)
    VedaKernelPlugin (SK KernelFunction)
    依赖 Semantic Kernel
  end note
}

rectangle "Veda.MCP\n[MCP Server]" as MCP #FFE8F0 {
  note as MCPNote
    KnowledgeBaseTools
    IngestTools
    MCP HTTP/SSE 端点
  end note
}

rectangle "Veda.Evaluation\n[评估框架]" as Eval #F0F8FF {
  note as EvalNote
    EvaluationRunner
    FaithfulnessScorer (LLM)
    AnswerRelevancyScorer (Embedding)
    ContextRecallScorer (Embedding)
    EvalDatasetRepository
  end note
}

rectangle "Veda.Api\n[入口层]" as Api #E8F4FD {
  note as ApiNote
    REST Controllers
    GraphQL (HotChocolate)
    SSE QueryStreamController
    DataSourceSyncBackgroundService
    Program.cs (DI 装配)
  end note
}

' 依赖关系
Storage  --> Core    : 实现接口
Prompts  --> Core    : 实现接口
Services --> Core    : 依赖接口
Services --> Storage : 依赖 IVectorStore 等接口实现
Services --> Prompts : 依赖 IContextWindowBuilder / IChainOfThoughtStrategy
Agents   --> Core    : 依赖接口
Agents   --> Storage : 依赖 IVectorStore（Plugin 直接查询）
Agents   --> Services: 依赖 IQueryService / IDocumentIngestor
MCP      --> Core    : 依赖接口
MCP      --> Services: 依赖 IDocumentIngestor / IEmbeddingService
Eval     --> Core    : 依赖接口
Eval     --> Services: 依赖 IQueryService / IChatService / IEmbeddingService
Api      --> Services: 依赖所有服务接口
Api      --> Agents  : 依赖 IOrchestrationService
Api      --> MCP     : 注册 MCP 服务
Api      --> Eval    : 依赖 IEvaluationRunner

note bottom of Core
  DIP 核心：
  所有接口在 Core 定义
  具体实现分散在各项目
  上层只依赖接口，不感知实现
end note

@enduml
```

---

## 2. DI 注册流程（Program.cs 装配顺序）

```plantuml
@startuml di-registration
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title DI 服务注册顺序（Program.cs）

rectangle "Program.cs" as Prog #E8F4FD {
  note as PNote
    1. AddVedaAiServices(cfg)
       → EmbeddingService (IEmbeddingGenerator<>)
       → OllamaChatService / AzureOpenAI
       → TextDocumentProcessor
       → LlmRouterService
       → HallucinationGuardService
       → DocumentIngestService
       → QueryService
       → 文件提取器 (DocIntelligence / Vision)
       → SemanticEnhancer (Noop or Personal)

    2. Configure<RagOptions> / VedaOptions / ...

    3. AddVedaStorage(cfg)
       → VedaDbContext (SQLite)
       → IVectorStore (Sqlite or CosmosDB)
       → ISemanticCache (Sqlite or CosmosDB)
       → ISyncStateStore / IPromptTemplateRepository
       → IUserMemoryStore
       → KnowledgeGovernanceService

    4. AddVedaPrompts()
       → ContextWindowBuilder
       → ChainOfThoughtStrategy

    5. AddVedaAgents()
       → Semantic Kernel (Kernel)
       → LlmOrchestrationService

    6. AddVedaEvaluation()
       → EvaluationRunner
       → FaithfulnessScorer
       → AnswerRelevancyScorer
       → ContextRecallScorer

    7. DataSource Connectors
       → FileSystemConnector
       → BlobStorageConnector
       → DataSourceSyncBackgroundService (Hosted)

    8. AddVedaMcp()
       → MCP Server 工具注册

    9. AddControllers / AddGraphQL / AddHealthChecks
       → CORS / RateLimit / Swagger
  end note
}

@enduml
```

---

## 3. 接口隔离（ISP）体现

```plantuml
@startuml isp-interfaces
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 11

title 接口隔离原则（ISP）在 Veda.Core 中的体现

rectangle "写操作接口" as Write #E8F8E8 {
  interface IDocumentIngestor {
    IngestAsync(content, name, type)
    IngestFileAsync(stream, name, mime, type)
  }
}

rectangle "读操作接口" as Read #E8F4FD {
  interface IQueryService {
    QueryAsync(request)
    QueryStreamAsync(request)
  }
}

rectangle "存储分离" as StorISP #FFF3CD {
  interface IVectorStore {
    UpsertAsync / UpsertBatchAsync
    SearchAsync / SearchByKeywordsAsync
    MarkDocumentSupersededAsync
    GetCurrentChunksByDocumentNameAsync
    ...
  }
  interface ISemanticCache {
    GetAsync / SetAsync / ClearAsync
  }
  interface IUserMemoryStore {
    RecordEventAsync
    GetBoostFactorAsync
    GetTermPreferencesAsync
  }
}

rectangle "AI 服务分离" as AiISP #F8E8FF {
  interface IEmbeddingService {
    GenerateEmbeddingAsync
    GenerateEmbeddingsAsync
  }
  interface IChatService {
    CompleteAsync
    CompleteStreamAsync
  }
  interface ILlmRouter {
    Resolve(QueryMode)
  }
}

rectangle "Prompt 构建分离" as PromptISP #FFF8E8 {
  interface IContextWindowBuilder {
    Build(candidates, maxTokens)
  }
  interface IChainOfThoughtStrategy {
    Enhance(question, context)
  }
}

note right of Write
  DocumentsController
  只依赖 IDocumentIngestor
  不知道 IQueryService 的存在
end note

note right of Read
  QueryController
  只依赖 IQueryService
  不知道 IDocumentIngestor 的存在
end note

note right of StorISP
  SemanticCache 比 VectorStore 更轻量
  独立接口让消费者只依赖所需能力
end note

@enduml
```

---

## 4. 可插拔扩展点一览

| 扩展点 | 当前实现 | 可替换为 | 切换方式 |
|--------|---------|---------|---------|
| 向量存储 | `SqliteVectorStore` | `CosmosDbVectorStore` / Azure AI Search | `Veda:StorageProvider` 配置 |
| Embedding 模型 | `bge-m3` (Ollama) | `text-embedding-3-small` (Azure OpenAI) | `Veda:EmbeddingProvider` 配置 |
| Chat 模型 (Simple) | `qwen3:8b` (Ollama) | `gpt-4o-mini` (Azure OpenAI) | `Veda:LlmProvider` 配置 |
| Chat 模型 (Advanced) | `deepseek-chat` | 任何 OpenAI 兼容端点 | `Veda:DeepSeek:*` 配置 |
| 文件提取（普通） | `DocumentIntelligenceFileExtractor` | 其他 OCR 服务 | 实现 `IFileExtractor` |
| 文件提取（图文） | `VisionModelFileExtractor` | 其他 Vision 模型 | 实现 `IFileExtractor` |
| 分块策略 | `TextDocumentProcessor` | AST-aware / Markdown-aware 分块器 | 实现 `IDocumentProcessor` |
| 语义增强 | `NoOpSemanticEnhancer` / `PersonalVocabularyEnhancer` | LLM 主动扩展 | 实现 `ISemanticEnhancer` |
| 重排序 | 轻量关键词覆盖率 | Cross-encoder 模型 | 修改 `QueryService.Rerank()` |
| Prompt 模板 | 硬编码 fallback / DB 动态 | 任何模板存储 | `IPromptTemplateRepository` |
| 数据源 | `FileSystemConnector` / `BlobStorageConnector` | SharePoint / Notion / 数据库 | 实现 `IDataSourceConnector` |
| Agent 编排 | 手动链 `OrchestrationService` / IRCoT `LlmOrchestrationService` | Multi-agent群组 | 实现 `IOrchestrationService` |
