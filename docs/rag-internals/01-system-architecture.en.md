> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[01-system-architecture.cn.md](01-system-architecture.cn.md)

# 01 — System Architecture

> This document describes VedaAide's overall architecture: code layering, module responsibilities, and Azure cloud infrastructure.

---

## 1. Code Layer Overview

The system consists of 8 C# projects arranged in four layers: "Core → Services → Infrastructure → Entry Points".

```plantuml
@startuml system-layers
skinparam backgroundColor #FAFAFA
skinparam componentStyle rectangle
skinparam defaultFontSize 13

title VedaAide — Code Layer Architecture

package "Entry Points" #E8F4FD {
  [Veda.Api\nREST / GraphQL / SSE / MCP HTTP]
  [Veda.Web\nFrontend (Blazor)]
}

package "Agent Orchestration Layer" #FFF3CD {
  [Veda.Agents\nIRCoT Orchestration / SK ChatCompletionAgent\nVedaKernelPlugin]
  [Veda.MCP\nMCP Server Tools\nsearch / ingest / list]
  [Veda.Evaluation\nEvaluation Framework\nFaithfulness / Relevancy / Recall]
}

package "Core Service Layer (Domain Services)" #E8F8E8 {
  [Veda.Services\nDocumentIngestService\nQueryService / HybridRetriever\nEmbeddingService / LlmRouter\nDataSource Connectors]
  [Veda.Prompts\nContextWindowBuilder\nChainOfThoughtStrategy]
}

package "Storage Layer (Infrastructure)" #F8E8FF {
  [Veda.Storage\nSqliteVectorStore\nCosmosDbVectorStore\nSemanticCache / UserMemoryStore\nKnowledgeGovernanceService]
}

package "Domain Core" #FFF8E8 {
  [Veda.Core\nDomain Models / Interface Contracts\nDocumentChunk / RagQueryRequest\nIVectorStore / IEmbeddingService\n...all IXxx interfaces]
}

' Dependency direction (arrow points to the depended-on module)
[Veda.Api]           --> [Veda.Services]
[Veda.Api]           --> [Veda.Agents]
[Veda.Api]           --> [Veda.MCP]
[Veda.Api]           --> [Veda.Evaluation]
[Veda.Agents]        --> [Veda.Services]
[Veda.Agents]        --> [Veda.Storage]
[Veda.MCP]           --> [Veda.Services]
[Veda.Evaluation]    --> [Veda.Services]
[Veda.Services]      --> [Veda.Prompts]
[Veda.Services]      --> [Veda.Storage]
[Veda.Services]      --> [Veda.Core]
[Veda.Prompts]       --> [Veda.Core]
[Veda.Storage]       --> [Veda.Core]
[Veda.Evaluation]    --> [Veda.Core]

note right of [Veda.Core]
  All interfaces defined here.
  Every layer depends downward only.
  —DIP (Dependency Inversion Principle)
end note

@enduml
```

---

## 2. Project Responsibilities

| Project | Responsibility | Key Classes |
|---------|---------------|-------------|
| **Veda.Core** | Domain models + interface contracts, zero external dependencies | `DocumentChunk`, `RagQueryRequest/Response`, `IVectorStore`, `IEmbeddingService`, `IQueryService`, `IDocumentIngestor` … |
| **Veda.Services** | Core RAG business logic, depends on abstract interfaces | `DocumentIngestService`, `QueryService`, `EmbeddingService`, `HybridRetriever`, `LlmRouterService`, `HallucinationGuardService`, `FileSystemConnector`, `BlobStorageConnector` |
| **Veda.Prompts** | Prompt construction strategies, LLM-agnostic | `ContextWindowBuilder` (token budget trimming), `ChainOfThoughtStrategy` (CoT injection) |
| **Veda.Storage** | Storage implementations, pluggable SQLite / CosmosDB | `SqliteVectorStore`, `CosmosDbVectorStore`, `SqliteSemanticCache`, `CosmosDbSemanticCache`, `UserMemoryStore`, `KnowledgeGovernanceService` |
| **Veda.Agents** | LLM Agent orchestration (Semantic Kernel) | `LlmOrchestrationService` (IRCoT loop), `OrchestrationService` (manual chain), `VedaKernelPlugin` (SK KernelFunction) |
| **Veda.MCP** | MCP Server: exposes knowledge base capabilities to external LLMs | `KnowledgeBaseTools` (search / list), `IngestTools` (ingest) |
| **Veda.Evaluation** | RAG evaluation framework (Golden Dataset) | `EvaluationRunner`, `FaithfulnessScorer`, `AnswerRelevancyScorer`, `ContextRecallScorer` |
| **Veda.Api** | HTTP entry point: REST + GraphQL + SSE | `DocumentsController`, `QueryController`, `QueryStreamController`, `DataSourcesController`; background service `DataSourceSyncBackgroundService` |

---

## 3. Runtime Request Path Overview

```plantuml
@startuml runtime-overview
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam componentStyle rectangle

title VedaAide — Runtime Request Path Overview

actor "User / External LLM" as Client

package "Veda.Api" #E8F4FD {
  component "REST Controller" as REST
  component "GraphQL Query" as GQL
  component "SSE Stream" as SSE
}

package "Veda.MCP" #FFF3CD {
  component "MCP Tools\n(HTTP/SSE)" as MCPTools
}

package "Veda.Agents" #FFF3CD {
  component "IRCoT Agent Loop" as AgentLoop
}

package "Veda.Services" #E8F8E8 {
  component "DocumentIngestService" as IngestSvc
  component "QueryService" as QuerySvc
}

package "External AI Services" #F0F0F0 {
  component "Azure OpenAI / Ollama\n(Embedding + Chat)" as AI
  component "Azure Document Intelligence\n(PDF/Image OCR)" as DI
}

database "SQLite\n(Local Dev)" as SQLite
database "CosmosDB\n(Production)" as CosmosDB

Client --> REST : POST /api/documents\nPOST /api/query
Client --> MCPTools : MCP Protocol
Client --> GQL : POST /graphql

REST  --> IngestSvc
REST  --> QuerySvc
MCPTools --> IngestSvc
MCPTools --> QuerySvc
GQL   --> QuerySvc
SSE   --> QuerySvc
AgentLoop --> QuerySvc

IngestSvc --> AI : Generate Embeddings
IngestSvc --> DI : Extract Text
IngestSvc --> SQLite
IngestSvc --> CosmosDB

QuerySvc --> AI : Embedding + Chat
QuerySvc --> SQLite
QuerySvc --> CosmosDB

@enduml
```

---

## 4. Azure Cloud Infrastructure

```plantuml
@startuml azure-infra
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam componentStyle rectangle

title VedaAide — Azure Infrastructure (Production)

cloud "Azure" {
  package "Azure Container Apps Environment" #E8F4FD {
    [vedaaide-api\nContainer App\n(Veda.Api)] as API
  }

  package "AI Services" #FFF3CD {
    [Azure OpenAI\ntext-embedding-3-small\ngpt-4o-mini] as AOI
    [Azure AI Document Intelligence\nprebuilt-invoice\nprebuilt-read] as DI
  }

  package "Storage" #E8F8E8 {
    database "Azure CosmosDB for NoSQL\nVectorChunks container (DiskANN)\nSemanticCache container" as COSMOS
    [Azure Blob Storage\nDocument data source] as BLOB
  }

  package "Observability" #F8E8FF {
    [Log Analytics\nContainer App Logs] as LOG
  }
}

actor "User / External LLM" as User

User --> API : HTTPS
API  --> AOI    : Embedding + Chat
API  --> DI     : File content extraction
API  --> COSMOS : Vector store / retrieval
API  --> BLOB   : Data source sync (BlobStorageConnector)
API  --> LOG    : Structured logging

note bottom of COSMOS
  Partition Key = /documentId
  Vector index: DiskANN (cosine distance)
  Dimensions: 1536 (text-embedding-3-small)
end note

note bottom of API
  Local dev: SQLite replaces CosmosDB
  Ollama replaces Azure OpenAI
  Switch via StorageProvider / LlmProvider config
end note

@enduml
```

---

## 5. SOLID Principles in Code

| Principle | Where it appears |
|-----------|-----------------|
| **DIP** (Dependency Inversion) | All interfaces defined in `Veda.Core`; `Veda.Services` depends on interfaces, not implementations |
| **SRP** (Single Responsibility) | `IDocumentIngestor` (write) and `IQueryService` (read) are separate; `ContextWindowBuilder` only does token trimming |
| **ISP** (Interface Segregation) | `IVectorStore` read/write split; `IFileExtractor` separate from `IDocumentProcessor` |
| **OCP** (Open/Closed) | Storage layer switches via `Veda:StorageProvider` config without modifying business code; same for LLM provider |
| **DRY** | `UpsertAsync` delegates to `UpsertBatchAsync`; hash calculation encapsulated in `ComputeHash` |
