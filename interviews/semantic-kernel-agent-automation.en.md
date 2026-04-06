# Interview Question: How to Implement Task Automation with Semantic Kernel

**Question**: In complex business logic, how would you leverage Semantic Kernel or Microsoft Agent Framework to implement task automation?

---

## Answer Approach

In real projects, I choose between two architectural patterns based on task **determinism** and **complexity**:

1. **LLM-Driven Autonomous Agents** — Suitable for complex reasoning and dynamic decision-making scenarios
2. **Manually Orchestrated Agent Chains** — Suitable for deterministic workflows requiring precise control

Below, I'll demonstrate both patterns using actual implementations from the VedaAide.NET project.

---

## Approach 1: LLM-Driven Autonomous Agents

**Core**: `ChatCompletionAgent` + `FunctionChoiceBehavior.Auto()` — LLM autonomously decides when to invoke tools

### Use Cases
- User questions require multiple retrieval rounds to answer
- Cannot predetermine how many tool calls are needed
- Requires LLM dynamic reasoning (Reason-Act-Observe loop)

### VedaAide Implementation: IRCoT (Interleaved Retrieval + Chain-of-Thought)

**Step 1: Encapsulate Business Capabilities as KernelPlugin**

```csharp
// VedaKernelPlugin.cs
public sealed class VedaKernelPlugin(IEmbeddingService embeddingService, IVectorStore vectorStore)
{
    [KernelFunction("search_knowledge_base")]
    [Description("Search the VedaAide knowledge base for relevant document chunks based on a natural language query.")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("The natural language query to search for relevant information")] string query,
        [Description("Maximum number of results to return (1-10)")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        // Vectorize query
        var embedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        
        // Vector search (minSimilarity=0.3 ensures quality)
        var chunks = await vectorStore.SearchAsync(
            embedding, topK: topK, minSimilarity: 0.3f, ct: cancellationToken);

        if (!chunks.Any())
            return "No relevant documents found in the knowledge base for this query.";

        // Format results (with source attribution + similarity scores)
        return string.Join("\n\n---\n\n", chunks.Select((c, i) =>
            $"[Source {i + 1}: {c.Chunk.DocumentName} (similarity: {c.Similarity:P0})]\n{c.Chunk.Content}"));
    }
}
```

**Key Design Points**:
- `[KernelFunction]` + `[Description]` — Helps LLM understand tool purpose
- Parameter `[Description]` — Guides LLM to pass correct arguments
- Returns formatted text (not structured data) — LLM can directly understand

---

**Step 2: Create Agent and Enable Autonomous Tool Calling**

```csharp
// LlmOrchestrationService.cs
public async Task<OrchestrationResult> RunQueryFlowAsync(
    string question, CancellationToken ct = default)
{
    var trace = new List<string>();
    trace.Add("QueryAgent (LLM): starting agent loop with IRCoT");

    // 1. Clone kernel to ensure request-level plugin isolation
    var agentKernel = kernel.Clone();
    agentKernel.Plugins.AddFromObject(
        new VedaKernelPlugin(embeddingService, vectorStore),
        pluginName: "KnowledgeBase");

    // 2. Create Agent with Instructions
    var agent = new ChatCompletionAgent
    {
        Name         = "QueryAgent",
        Instructions = """
            You are VedaAide, an intelligent knowledge-base assistant.
            When answering questions, follow these steps:
            1. ALWAYS call search_knowledge_base first to retrieve relevant information.
            2. If the initial results are insufficient, refine your search query and try again.
            3. Synthesize the retrieved information into a clear, accurate, concise answer.
            4. If the knowledge base contains no relevant information, say so explicitly.
            Think step by step before giving your final answer.
            """,
        Kernel       = agentKernel,
        Arguments    = new KernelArguments(new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()  // Core: LLM autonomous decision-making
        })
    };

    // 3. Execute Agent loop
    var sb = new System.Text.StringBuilder();
    var thread = new ChatHistoryAgentThread();

    await foreach (var item in agent.InvokeAsync(
        new ChatMessageContent(AuthorRole.User, question),
        thread: thread,
        cancellationToken: ct))
    {
        if (item.Message.Content is not null)
            sb.Append(item.Message.Content);
    }

    // 4. Extract tool call information from conversation history (observability)
    int toolCallCount = 0;
    var toolSources = new List<SourceReference>();

    foreach (var msg in thread.ChatHistory)
    {
        if (msg.Role == AuthorRole.Tool && msg.Content is { Length: > 0 })
        {
            toolCallCount++;
            toolSources.Add(new SourceReference
            {
                DocumentName = "knowledge-base",
                ChunkContent = msg.Content.Length > 300 
                    ? msg.Content[..300] + "…" 
                    : msg.Content,
                Similarity = 0f
            });
        }
    }

    if (toolCallCount > 0)
        trace.Add($"QueryAgent: invoked search_knowledge_base {toolCallCount} time(s)");
    else
        trace.Add("QueryAgent: completed without tool calls");

    var answer = sb.ToString().Trim();
    trace.Add($"QueryAgent: final answer generated ({answer.Length} chars)");

    // 5. EvalAgent — Hallucination detection (detailed below)
    string? evalSummary = null;
    if (toolSources.Count > 0)
    {
        var context = string.Join("\n\n", toolSources.Select(s => s.ChunkContent));
        var isGrounded = await hallucinationGuard.VerifyAsync(answer, context, ct);
        evalSummary = isGrounded
            ? "EvalAgent: answer is grounded in source documents ✓"
            : "EvalAgent: answer may not be fully supported by retrieved context ⚠";
        trace.Add(evalSummary);
    }

    return new OrchestrationResult
    {
        Answer            = answer,
        IsEvaluated       = evalSummary is not null,
        EvaluationSummary = evalSummary,
        AgentTrace        = trace.AsReadOnly()  // Complete execution trace
    };
}
```

**Workflow**:
```
User: "How is VedaAide's RAG pipeline implemented?"
    ↓
QueryAgent (LLM reasoning):
  Thinking: "This is about system implementation, need to retrieve technical docs"
  → call search_knowledge_base(query="VedaAide RAG pipeline implementation", topK=5)
  ← Returns 5 document fragments (with source attribution)
  Thinking: "Retrieved content is comprehensive, can synthesize answer"
  → Generate: "VedaAide's RAG pipeline includes the following modules: 1. DocumentIngestService..."
    ↓
EvalAgent (LLM fact-checking):
  Verify: Is every claim in the answer supported by retrieved results?
  → Returns: true (grounded)
    ↓
Final return: Answer + Sources + EvaluationSummary + AgentTrace
```

**Advantages**:
- ✅ **Zero Hard-coding** — No need to predict how many retrievals needed
- ✅ **Adaptive** — LLM decides whether to retrieve again based on result quality
- ✅ **Explainable** — `AgentTrace` records decision path

---

## Approach 2: Manually Orchestrated Agent Chains

**Core**: Predefined execution order, each Agent has single responsibility (SRP principle)

### Use Cases
- Deterministic workflows (document ingestion, multi-agent collaboration)
- Requires precise execution order control
- Production environments need testability and observability

### VedaAide Implementation: QueryAgent → EvalAgent Pipeline

```csharp
// OrchestrationService.cs
public async Task<OrchestrationResult> RunQueryFlowAsync(
    string question, CancellationToken ct = default)
{
    var trace = new List<string>();

    // Step 1: QueryAgent — RAG retrieval + LLM generation
    trace.Add("QueryAgent: executing RAG pipeline");
    var request = new RagQueryRequest { Question = question };
    var response = await queryService.QueryAsync(request, ct);
    trace.Add($"QueryAgent: answer generated (confidence={response.AnswerConfidence:P0}, hallucination={response.IsHallucination})");

    // Step 2: EvalAgent — Hallucination detection (LLM self-verification)
    string? evalSummary = null;
    if (!response.IsHallucination && response.Sources.Count > 0)
    {
        var context = string.Join("\n\n", response.Sources.Select(s => s.ChunkContent));
        var isGrounded = await hallucinationGuard.VerifyAsync(response.Answer, context, ct);
        evalSummary = isGrounded
            ? "EvalAgent: answer is grounded in source documents ✓"
            : "EvalAgent: answer may not be fully supported by retrieved context ⚠";
        trace.Add(evalSummary);
    }
    else
    {
        trace.Add("EvalAgent: skipped (hallucination detected or no sources)");
    }

    return new OrchestrationResult
    {
        Answer            = response.Answer,
        IsEvaluated       = evalSummary is not null,
        EvaluationSummary = evalSummary,
        AgentTrace        = trace.AsReadOnly()
    };
}
```

**EvalAgent Implementation: HallucinationGuardService**

```csharp
// HallucinationGuardService.cs
public sealed class HallucinationGuardService(IChatService chatService) : IHallucinationGuardService
{
    private const string SystemPrompt = """
        You are a strict fact-checking assistant.
        Your task: determine whether the Answer below is FULLY SUPPORTED by the provided Context.
        Rules:
        - Respond ONLY with "true" if every claim in the Answer can be found in or directly inferred from the Context.
        - Respond ONLY with "false" if the Answer contains ANY claim not present in the Context.
        - Do not add any explanation or extra text.
        """;

    public async Task<bool> VerifyAsync(string answer, string context, CancellationToken ct = default)
    {
        var userMessage = $"Context:\n{context}\n\nAnswer to verify:\n{answer}";
        var response = await chatService.CompleteAsync(SystemPrompt, userMessage, ct);
        return response.Trim().StartsWith("true", StringComparison.OrdinalIgnoreCase);
    }
}
```

**Execution Flow**:
```
User Question
    ↓
QueryAgent:
  - Embedding → Vector Search → LLM Generate
  - Returns: Answer + Sources + Confidence
    ↓
EvalAgent (HallucinationGuardService):
  - LLM as fact-checker
  - System Prompt: "Is every claim supported by Context?"
  - Returns: true/false
    ↓
OrchestrationResult:
  - Answer
  - IsEvaluated = true
  - EvaluationSummary = "answer is grounded ✓"
  - AgentTrace = ["QueryAgent: ...", "EvalAgent: ..."]
```

**Advantages**:
- ✅ **Testable** — Each Agent can be unit-tested independently
- ✅ **Debuggable** — Fixed order, breakpoint debugging friendly
- ✅ **Observable** — `AgentTrace` records every step
- ✅ **Reliable** — Deterministic execution, no LLM randomness

---

## Real-World Example: Two-Layer Hallucination Defense

VedaAide's hallucination detection uses a **two-layer architecture** (defense in depth):

### Layer 1: Embedding Cosine Similarity (Fast Filtering)

```csharp
// QueryService.cs
private async Task<bool> CheckHallucination(string answer, IReadOnlyList<SourceReference> sources, CancellationToken ct)
{
    if (sources.Count == 0) return true;  // No sources → hallucination

    // Vectorize answer
    var answerEmbedding = await embeddingService.GenerateEmbeddingAsync(answer, ct);

    // Calculate cosine similarity with all source fragments
    var maxSimilarity = 0f;
    foreach (var source in sources)
    {
        if (source.ChunkEmbedding is null) continue;
        var sim = VectorMath.CosineSimilarity(answerEmbedding, source.ChunkEmbedding);
        if (sim > maxSimilarity) maxSimilarity = sim;
    }

    // Below threshold → hallucination
    return maxSimilarity < options.Value.HallucinationSimilarityThreshold;  // default 0.6
}
```

### Layer 2: LLM Self-Verification (Precise Validation)

Only called when Layer 1 passes + `EnableSelfCheckGuard=true`:

```csharp
// In QueryService
if (options.Value.EnableSelfCheckGuard)
{
    var selfCheckPassed = await hallucinationGuard.VerifyAsync(answer, context, ct);
    if (!selfCheckPassed)
    {
        logger.LogWarning("Answer failed LLM self-check guard");
        isHallucination = true;
    }
}
```

**Effect**:
- Layer 1 blocks obviously deviated answers (fast, low-cost)
- Layer 2 reviews detailed accuracy sentence-by-sentence (rigorous, high-quality)
- Combined: balances speed + quality

---

## Key Design Patterns for Agent Orchestration

### 1. Plugin Isolation (Request-Level Isolation)

```csharp
var agentKernel = kernel.Clone();  // ✅ Independent kernel instance per request
agentKernel.Plugins.AddFromObject(...);
```

**Why**: Prevents plugin pollution across concurrent requests, ensures thread safety.

---

### 2. Reason-Act-Observe Loop

```
LLM Reasoning: "Need to retrieve documents"
    ↓
Action: call search_knowledge_base(...)
    ↓
Observation: Retrieval results returned
    ↓
LLM Reasoning: "Results insufficient, try different keywords"
    ↓
Action: call search_knowledge_base(...)
    ↓
...
```

This is the essence of **IRCoT (Interleaved Retrieval + Chain-of-Thought)**.

---

### 3. SRP (Single Responsibility Principle)

Each Agent in VedaAide has a single responsibility:

| Agent | Responsibility | Implementation |
|-------|----------------|----------------|
| **QueryAgent** | Retrieval + answer generation | `QueryService.QueryAsync()` |
| **EvalAgent** | Answer quality verification | `HallucinationGuardService.VerifyAsync()` |
| **DocumentAgent** | Document ingestion | `DocumentIngestor.IngestAsync()` |

**Benefits**: Easy to test, maintain, and extend.

---

### 4. Observable Agent Chain

```csharp
public class OrchestrationResult
{
    public string Answer { get; init; } = string.Empty;
    public bool IsEvaluated { get; init; }
    public string? EvaluationSummary { get; init; }
    public IReadOnlyList<string> AgentTrace { get; init; } = Array.Empty<string>();
    //                                        ^^^^^^^^^^^ Complete execution trace
}
```

**AgentTrace Example**:
```
QueryAgent: executing RAG pipeline
QueryAgent: answer generated (confidence=87%, hallucination=False)
EvalAgent: answer is grounded in source documents ✓
```

Used for debugging, monitoring, and auditing.

---

## Production Architecture Choices

In real projects, I **combine both patterns**:

```
Outer Layer: Manual Orchestration (OrchestrationService)
  ├─ QueryAgent (Inner: LLM autonomous reasoning with ChatCompletionAgent)
  ├─ EvalAgent (Deterministic: direct call to HallucinationGuardService)
  └─ AuditAgent (Deterministic: logging + database writes)
```

**Principles**:
- **Needs LLM dynamic reasoning** → Use `ChatCompletionAgent` + `FunctionChoiceBehavior.Auto()`
- **Deterministic workflows** → Manual orchestration
- **Production must-haves** → Full trace + LLM verification layer

---

## Summary

In the VedaAide.NET project, I leverage Semantic Kernel for task automation through:

1. **Plugin Encapsulation** — Wrap complex RAG retrieval as `[KernelFunction]`, enabling direct LLM invocation
2. **Auto Function Calling** — `FunctionChoiceBehavior.Auto()` implements IRCoT without hard-coding retrieval timing
3. **Agent Chain Orchestration** — QueryAgent → EvalAgent achieves two-layer hallucination defense
4. **Plugin Isolation** — `kernel.Clone()` ensures request-level safety
5. **Observability** — `AgentTrace` records complete decision paths for debugging and auditing

This architecture ensures both **flexibility** (LLM autonomous reasoning) and **controllability** (deterministic pipelines), validated in production environments.

---

## Code Locations

- **LLM-Driven Agent**: `src/Veda.Agents/LlmOrchestrationService.cs`
- **Manually Orchestrated Agent**: `src/Veda.Agents/Orchestration/OrchestrationService.cs`
- **KernelPlugin**: `src/Veda.Agents/VedaKernelPlugin.cs`
- **Hallucination Detection**: `src/Veda.Services/HallucinationGuardService.cs`
- **RAG Core**: `src/Veda.Services/QueryService.cs`
