# Phase 4 Design: MCP + Agentic Workflow

> Prerequisite: Phase 3 is complete (GraphQL + SSE streaming + Angular frontend + Docker deployment).

> 中文版见 [phase4-mcp-agents.cn.md](phase4-mcp-agents.cn.md)

---

## 1. Goals and Deliverables

| Deliverable | Description |
|--------|------|
| `Veda.MCP` | MCP Server library exposing knowledge base tools, integrated into `Veda.Api` |
| `Veda.Agents` | Semantic Kernel Agent orchestration layer, multi-agent collaboration |
| `Veda.Prompts` (completed) | Prompt template library + context window builder |
| `.vscode/mcp.json` | VSCode Copilot Chat integration configuration |

---

## 2. Veda.MCP — MCP Server

### 2.1 Technology Choices

| Item | Choice | Rationale |
|------|------|------|
| MCP SDK | `ModelContextProtocol.AspNetCore` (Microsoft official) | Native .NET, same ecosystem as SK |
| Transport (dev) | HTTP Streamable (`/mcp`) | Reuses the same port as Veda.Api, no separate process |
| Transport (alternative) | stdio | For direct local VSCode invocation |

### 2.2 Exposed MCP Tools

| Tool Name | Input | Output | Purpose |
|---------|------|------|------|
| `search_knowledge_base` | `query: string`, `topK: int = 5` | JSON array (chunk + similarity) | Vector search |
| `ingest_document` | `content: string`, `documentName: string`, `documentType: string` | `{ documentId, chunksAdded }` | Ingest text |
| `list_documents` | none | JSON array (document name + chunk count) | View knowledge base contents |

### 2.3 Project Structure

```
src/Veda.MCP/
  Veda.MCP.csproj
  GlobalUsings.cs
  McpServiceExtensions.cs          # AddVedaMcp() extension method
  Tools/
    KnowledgeBaseTools.cs          # search + list
    IngestTools.cs                 # ingest
```

### 2.4 Key Code Pattern

```csharp
// McpServiceExtensions.cs
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<KnowledgeBaseTools>()
    .WithTools<IngestTools>();

// Program.cs
app.MapMcp("/mcp");

// Tool definition
[McpServerToolType]
public sealed class KnowledgeBaseTools(IEmbeddingService embedding, IVectorStore vectorStore)
{
    [McpServerTool, Description("Search the VedaAide knowledge base")]
    public async Task<string> SearchKnowledgeBase(
        [Description("Search query")] string query,
        [Description("Max results")] int topK = 5)
    {
        var embedding = await _embedding.GenerateEmbeddingAsync(query);
        var results = await _vectorStore.SearchAsync(embedding, topK);
        return JsonSerializer.Serialize(results.Select(r => new { r.Chunk.Content, r.Chunk.DocumentName, r.Similarity }));
    }
}
```

---

## 3. Veda.Agents — Semantic Kernel Agent Orchestration

### 3.1 Technology Choices

Using **Semantic Kernel Agents** (`Microsoft.SemanticKernel.Agents.Core`, built into SK 1.73) — no additional Agent Framework needed.

### 3.2 Agent Roles

> ⚠️ **Implementation note**: The three Agent roles do not have separate class files — they are directly embedded in the orchestration services:
- Both `OrchestrationService` (deterministic) and `LlmOrchestrationService` (LLM-driven) handle DocumentAgent / QueryAgent / EvalAgent responsibilities.

| Agent Role | Responsibility | Implementation Location |
|------------|------|----------|
| DocumentAgent | Document type inference + ingestion | `OrchestrationService.RunIngestFlowAsync()` / `LlmOrchestrationService.RunIngestFlowAsync()` |
| QueryAgent | RAG retrieval + LLM generation | `OrchestrationService.RunQueryFlowAsync()` / `LlmOrchestrationService` (Agent Loop) |
| EvalAgent | Context consistency validation | `HallucinationGuardService.VerifyAsync()` call at the end of both OrchestrationService implementations |

### 3.3 Orchestration Pattern

```
User request → OrchestrationService.RunAsync()
    ├─ DocumentAgent (document processing scenario)
    └─ QueryAgent → EvalAgent (Q&A + auto-evaluation scenario)
```

### 3.4 Actual Project Structure

```
src/Veda.Agents/
  Veda.Agents.csproj
  GlobalUsings.cs
  AgentServiceExtensions.cs         # AddVedaAgents(), registers LlmOrchestrationService
  VedaKernelPlugin.cs               # search_knowledge_base KernelFunction (for ChatCompletionAgent)
  LlmOrchestrationService.cs        # LLM-driven Agent Loop (ChatCompletionAgent + IRCoT) — default implementation
  Orchestration/
    IOrchestrationService.cs        # Interface definition
    OrchestrationService.cs         # Deterministic call chain implementation (backup, easier to test)
```

> `AgentServiceExtensions.AddVedaAgents()` registers `LlmOrchestrationService` as the default implementation of `IOrchestrationService`.

---

## 4. Veda.Prompts — Prompt Engineering Layer

### 4.1 Implemented Content

| Class | Responsibility |
|----|------|
| `PromptTemplate` | Template entity (Name, Version, Content, DocumentType) |
| `PromptTemplateRepository` | Load/save versioned templates from EF Core |
| `ContextWindowBuilder` | Select optimal context from candidates within a token budget |
| `ServiceCollectionExtensions` | `AddVedaPrompts()` DI registration |

### 4.2 Integration with Existing Code

- `QueryService` uses `IContextWindowBuilder` interface to replace the previously hardcoded `Take(topK)` logic.
- `Veda.Core.Interfaces` now includes `IContextWindowBuilder`.

---

## 5. VSCode Copilot Chat Integration

### 5.1 `.vscode/mcp.json` Configuration

**Via HTTP (recommended — when Veda.Api is already running)**

```json
{
  "servers": {
    "vedaaide": {
      "type": "http",
      "url": "http://localhost:5126/mcp"
    }
  }
}
```

> The current `ModelContextProtocol.AspNetCore` uses Streamable HTTP protocol mounted at `/mcp`.

### 5.2 Verification Steps

1. Start `Veda.Api` (`dotnet run`).
2. Open VSCode Copilot Chat, switch to **Agent mode** (`@`).
3. Type: `@vedaaide search for documents about XX`.
4. Observe Tool call logs, confirm `search_knowledge_base` is triggered.

---

## 6. Database Changes (EF Core Migrations)

Phase 4 adds the `PromptTemplate` table:

```bash
dotnet ef migrations add Phase4_PromptTemplates --project src/Veda.Storage --startup-project src/Veda.Api
```

Phase 5 adds the `SyncedFiles` table for sync state tracking:

```bash
dotnet ef migrations add Phase5_SyncedFiles --project src/Veda.Storage --startup-project src/Veda.Api
```

> ✅ Both migrations have been generated. `Program.cs` calls `MigrateAsync()` on startup to apply them automatically.

---

## 7. Test Strategy

| Test Type | Location | Coverage | Status |
|---------|------|---------|------|
| MCP Tool unit tests | `tests/Veda.Services.Tests/` | Mock `IVectorStore`, verify `KnowledgeBaseTools` / `IngestTools` input/output format | ✅ Done |
| Agent orchestration unit tests | `tests/Veda.Services.Tests/` | Mock all dependencies, verify `OrchestrationService` call chain | ✅ Done |
| Prompt module unit tests | `tests/Veda.Core.Tests/` | `ContextWindowBuilder` token budget logic, `ChainOfThoughtStrategy` output format | ✅ Done |
| External data source unit tests | `tests/Veda.Services.Tests/` | `FileSystemConnector` normal/directory-not-found/disabled scenarios | ✅ Done |
| MCP integration tests | `tests/Veda.Services.Tests/` | Start in-memory WebApp, call `/mcp` to verify tool responses | ⏳ Pending |

---

## 8. Development Order

```
Step 1: Complete Veda.Prompts code (no external dependencies, simplest)
Step 2: Veda.MCP project + Tools definition
Step 3: Mount MCP to Veda.Api (Program.cs + csproj)
Step 4: .vscode/mcp.json + manual VSCode verification
Step 5: Veda.Agents project + Agent definition
Step 6: Update VedaAide.slnx
Step 7: Unit tests
```

---

## 9. Risks and Mitigations

| Risk | Mitigation |
|------|------|
| MCP SDK API changes (still actively iterating) | Lock NuGet package versions, run tests before upgrading |
| SK Agents API instability | Phase 4 uses manual call chain instead of `AgentGroupChat`, reducing dependency |
| MCP HTTP CORS issues | Configure AllowAll CORS policy for `/mcp/*` routes in `Veda.Api` |
| `PromptTemplate` table not migrated → startup failure | `Program.cs` already has `MigrateAsync()`, auto-creates tables |
