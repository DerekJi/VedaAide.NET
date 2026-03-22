# Agent Orchestration Patterns: Deterministic Call Chain vs. LLM-Driven Agent

**Phase**: Phase 4

> 中文版见 [agent-patterns.cn.md](agent-patterns.cn.md)

---

## Current Implementation (Phase 4 Basic)

`OrchestrationService` is a **hardcoded deterministic call chain** where execution order is fixed in code — it is not LLM-driven:

```
RunIngestFlowAsync:
  InferDocumentType(filename)       ← string matching, not LLM
  → DocumentIngestService.IngestAsync()

RunQueryFlowAsync:
  QueryService.QueryAsync()
  → HallucinationGuardService.VerifyAsync()
  → returns OrchestrationResult (with agentTrace execution log)
```

Each "Agent" is simply a wrapper around an existing Service. The calling order does not dynamically adjust based on the nature of the question.

## Truly Agentic Loop (Phase 4.5 / Done via LlmOrchestrationService)

A truly LLM-driven Agent requires three elements:
1. **LLM autonomously decides tool calls**: the LLM sees the available tool list and independently chooses which ones to call and how many times
2. **Iterative loop (Reason-Act-Observe)**: after each tool returns a result, the LLM re-evaluates whether to continue
3. **Conversation history maintenance**: context state is preserved across multiple rounds

Semantic Kernel implementation:
```csharp
var agent = new ChatCompletionAgent
{
    Kernel = kernel,
    Instructions = systemPrompt,
    Name = "QueryAgent"
};
kernel.Plugins.AddFromType<KnowledgeBasePlugin>();
// LLM autonomously decides when to call search_knowledge_base and how many times
await foreach (var message in agent.InvokeAsync(thread))
{
    // Each round may trigger a tool call
}
```

## Agent Responsibility Boundaries (The Correct Classification)

```
DocumentIngestAgent (content processing layer)
  ↑ delegated to
FileSystemIngestAgent (file system data source)
BlobStorageIngestAgent (Azure Blob data source)
DatabaseIngestAgent (database data source)  ← future extension
```

The design's "DocumentAgent" naming has an ambiguity:
- In the implementation it's `DocumentIngestAgent` (processes content: chunking, embedding, dedup, storage)
- Not a "document source Agent"
- Source Agents (FileSystem/Blob) are responsible for fetching content and delegating to `DocumentIngestAgent` for processing — SRP clearly separated

## Two Execution Timings for EvalAgent

| Scenario | Timing | Role |
|---|---|---|
| **Real-time Q&A** (Phase 4) | Run serially right after each single Query | Instant hallucination check on the current answer |
| **Batch testing** (Phase 5 Test Harness) | Run collectively after all Queries complete | Cross-comparison of evaluation scores across models/prompts |

Current `EvalAgent` = a wrapper around `HallucinationGuardService.VerifyAsync()` (vector similarity + optional LLM self-check).

Phase 5 upgrade: `FaithfulnessScorer` + `AnswerRelevancyScorer` running in parallel, supporting A/B test reports.
