> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[06-module-dependencies.cn.md](06-module-dependencies.cn.md)

# 06 — Module Dependency Topology

> Dependencies between VedaAide C# projects, and how SOLID principles manifest at project boundaries.

---

## 1. Project Dependency Topology

```plantuml
@startuml module-dependencies
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam componentStyle rectangle
skinparam ArrowColor #555555

title VedaAide — Project Dependency Topology (arrows = depended-on direction)

rectangle "Veda.Core\n[Domain Core]" as Core #FFF8E8 {
  note as CoreNote
    · Domain models: DocumentChunk, RagQueryRequest/Response
    · All interfaces: IVectorStore, IEmbeddingService,
      IQueryService, IDocumentIngestor, IChatService,
      IHybridRetriever, ISemanticCache, IChainOfThoughtStrategy,
      IContextWindowBuilder, IHallucinationGuardService,
      IFeedbackBoostService, IDocumentProcessor, IFileExtractor,
      ISemanticEnhancer, IDataSourceConnector ...
    · Pure math: VectorMath.CosineSimilarity
    · Value objects: ChunkingOptions, RagDefaults, KnowledgeScope
    Zero external NuGet dependencies (besides framework)
  end note
}

rectangle "Veda.Storage\n[Infrastructure Layer]" as Storage #F8E8FF {
  note as StorNote
    Implements IVectorStore, ISemanticCache,
    IUserMemoryStore, ISyncStateStore,
    IKnowledgeGovernanceService ...

    SqliteVectorStore / CosmosDbVectorStore
    SqliteSemanticCache / CosmosDbSemanticCache
    VedaDbContext (EF Core)
    KnowledgeGovernanceService
  end note
}

rectangle "Veda.Prompts\n[Prompt Construction]" as Prompts #E8F4FD {
  note as ProNote
    Implements IContextWindowBuilder,
    IChainOfThoughtStrategy

    ContextWindowBuilder (token trimming)
    ChainOfThoughtStrategy (CoT injection)
    No LLM dependency, pure string operations
  end note
}

rectangle "Veda.Services\n[Core Service Layer]" as Services #E8F8E8 {
  note as SvcNote
    Implements IDocumentIngestor, IQueryService,
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

rectangle "Veda.Agents\n[Agent Orchestration]" as Agents #FFF3CD {
  note as AgNote
    LlmOrchestrationService (IRCoT)
    OrchestrationService (manual chain)
    VedaKernelPlugin (SK KernelFunction)
    Depends on Semantic Kernel
  end note
}

rectangle "Veda.MCP\n[MCP Server]" as MCP #FFE8F0 {
  note as MCPNote
    KnowledgeBaseTools
    IngestTools
    MCP HTTP/SSE endpoints
  end note
}

rectangle "Veda.Evaluation\n[Evaluation Framework]" as Eval #F0F8FF {
  note as EvalNote
    EvaluationRunner
    FaithfulnessScorer (LLM)
    AnswerRelevancyScorer (Embedding)
    ContextRecallScorer (Embedding)
    EvalDatasetRepository
  end note
}

rectangle "Veda.Api\n[Entry Layer]" as Api #E8F4FD {
  note as ApiNote
    REST Controllers
    GraphQL (HotChocolate)
    SSE QueryStreamController
    DataSourceSyncBackgroundService
    Program.cs (DI assembly)
  end note
}

' Dependencies
Storage  --> Core    : implements interfaces
Prompts  --> Core    : implements interfaces
Services --> Core    : depends on interfaces
Services --> Storage : depends on IVectorStore etc.
Services --> Prompts : depends on IContextWindowBuilder / IChainOfThoughtStrategy
Agents   --> Core    : depends on interfaces
Agents   --> Storage : depends on IVectorStore (Plugin direct query)
Agents   --> Services: depends on IQueryService / IDocumentIngestor
MCP      --> Core    : depends on interfaces
MCP      --> Services: depends on IDocumentIngestor / IEmbeddingService
Eval     --> Core    : depends on interfaces
Eval     --> Services: depends on IQueryService / IChatService / IEmbeddingService
Api      --> Services: depends on all service interfaces
Api      --> Agents  : depends on IOrchestrationService
Api      --> MCP     : registers MCP service
Api      --> Eval    : depends on IEvaluationRunner

note bottom of Core
  DIP core:
  All interfaces defined in Core
  Implementations distributed across projects
  Upper layers depend on interfaces only,
  not aware of implementations
end note

@enduml
```

---

## 2. DI Registration Flow (Program.cs assembly order)

```plantuml
@startuml di-registration
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title DI Service Registration Order (Program.cs)

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
       → File extractors (DocIntelligence / Vision)
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

    7. Data Source Connectors
       → FileSystemConnector
       → BlobStorageConnector
       → DataSourceSyncBackgroundService (Hosted)

    8. AddVedaMcp()
       → MCP Server tool registration

    9. AddControllers / AddGraphQL / AddHealthChecks
       → CORS / RateLimit / Swagger
  end note
}

@enduml
```

---

## 3. Interface Segregation at Project Boundaries

| Interface Package | Defined In | Implemented In | Used By |
|------------------|-----------|----------------|---------|
| `IVectorStore` | `Veda.Core` | `Veda.Storage` | `Veda.Services`, `Veda.Agents` |
| `ISemanticCache` | `Veda.Core` | `Veda.Storage` | `Veda.Services` |
| `IEmbeddingService` | `Veda.Core` | `Veda.Services` | `Veda.Services`, `Veda.Evaluation`, `Veda.MCP` |
| `IQueryService` | `Veda.Core` | `Veda.Services` | `Veda.Agents`, `Veda.Evaluation`, `Veda.Api` |
| `IDocumentIngestor` | `Veda.Core` | `Veda.Services` | `Veda.Agents`, `Veda.MCP`, `Veda.Api` |
| `IChatService` | `Veda.Core` | `Veda.Services` | `Veda.Services` (via LlmRouter), `Veda.Evaluation` |
| `IContextWindowBuilder` | `Veda.Core` | `Veda.Prompts` | `Veda.Services` |
| `IChainOfThoughtStrategy` | `Veda.Core` | `Veda.Prompts` | `Veda.Services` |
| `IHallucinationGuardService` | `Veda.Core` | `Veda.Services` | `Veda.Services`, `Veda.Agents` |
| `IDataSourceConnector` | `Veda.Core` | `Veda.Services` | `Veda.Api` (via BgService) |
| `IEvaluationRunner` | `Veda.Core` | `Veda.Evaluation` | `Veda.Api` |
| `IOrchestrationService` | `Veda.Core` | `Veda.Agents` | `Veda.Api` |
