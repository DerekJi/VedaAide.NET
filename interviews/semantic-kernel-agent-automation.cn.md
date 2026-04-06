# 面试问题：如何利用 Semantic Kernel 实现任务自动化

**问题**：在复杂的业务逻辑中，你会如何利用 Semantic Kernel 或 Microsoft Agent Framework 来实现任务自动化？

---

## 回答思路

在实际项目中，我会根据任务的**确定性**和**复杂度**选择两种架构模式：

1. **LLM驱动的自主Agent** — 适合复杂推理、动态决策的场景
2. **手动编排的Agent链** — 适合确定性流程、需要精确控制的场景

下面我以 VedaAide.NET 项目中的实际实现为例，展示这两种模式的应用。

---

## 方案一：LLM驱动的自主Agent

**核心**：`ChatCompletionAgent` + `FunctionChoiceBehavior.Auto()` — LLM自主决定何时调用工具

### 适用场景
- 用户问题需要多轮检索才能解答
- 无法预先确定需要调用几次工具
- 需要 LLM 动态推理（Reason-Act-Observe循环）

### VedaAide 实现：IRCoT (Interleaved Retrieval + Chain-of-Thought)

**第一步：将业务能力封装为 KernelPlugin**

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
        // 向量化查询
        var embedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        
        // 向量检索（minSimilarity=0.3 确保质量）
        var chunks = await vectorStore.SearchAsync(
            embedding, topK: topK, minSimilarity: 0.3f, ct: cancellationToken);

        if (!chunks.Any())
            return "No relevant documents found in the knowledge base for this query.";

        // 格式化返回结果（带来源标注 + 相似度）
        return string.Join("\n\n---\n\n", chunks.Select((c, i) =>
            $"[Source {i + 1}: {c.Chunk.DocumentName} (similarity: {c.Similarity:P0})]\n{c.Chunk.Content}"));
    }
}
```

**关键设计**：
- `[KernelFunction]` + `[Description]` — 让 LLM 理解工具的用途
- 参数带 `[Description]` — 引导 LLM 正确传参
- 返回格式化文本（而非结构化数据） — LLM 能直接理解

---

**第二步：创建 Agent 并启用自主工具调用**

```csharp
// LlmOrchestrationService.cs
public async Task<OrchestrationResult> RunQueryFlowAsync(
    string question, CancellationToken ct = default)
{
    var trace = new List<string>();
    trace.Add("QueryAgent (LLM): starting agent loop with IRCoT");

    // 1. Clone kernel 确保 plugin 请求级隔离
    var agentKernel = kernel.Clone();
    agentKernel.Plugins.AddFromObject(
        new VedaKernelPlugin(embeddingService, vectorStore),
        pluginName: "KnowledgeBase");

    // 2. 创建 Agent with Instructions
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
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()  // 核心：LLM自主决策
        })
    };

    // 3. 执行 Agent 循环
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

    // 4. 从对话历史中提取工具调用信息（可观测性）
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

    // 5. EvalAgent — 幻觉检测（后文详述）
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
        AgentTrace        = trace.AsReadOnly()  // 完整执行链路
    };
}
```

**工作流程**：
```
User: "VedaAide的RAG pipeline是如何实现的？"
    ↓
QueryAgent (LLM reasoning):
  思考: "这是关于系统实现的问题，需要检索技术文档"
  → call search_knowledge_base(query="VedaAide RAG pipeline implementation", topK=5)
  ← 返回 5 个文档片段（带来源标注）
  思考: "检索到的内容较完整，可以综合作答"
  → 生成: "VedaAide的RAG pipeline包含以下模块：1. DocumentIngestService..."
    ↓
EvalAgent (LLM fact-checking):
  验证: 答案中的每个主张是否有检索结果支撑
  → 返回: true (grounded)
    ↓
最终返回: Answer + Sources + EvaluationSummary + AgentTrace
```

**优势**：
- ✅ **零硬编码** — 不需要预判需要几次检索
- ✅ **自适应** — LLM根据结果质量决定是否再次检索
- ✅ **可解释** — `AgentTrace` 记录决策路径

---

## 方案二：手动编排的Agent链

**核心**：预定义的执行顺序，每个Agent职责单一（SRP原则）

### 适用场景
- 确定性流程（摄取文档、多Agent协作）
- 需要精确控制执行顺序
- 生产环境需要可测试性、可观测性

### VedaAide 实现：QueryAgent → EvalAgent 流水线

```csharp
// OrchestrationService.cs
public async Task<OrchestrationResult> RunQueryFlowAsync(
    string question, CancellationToken ct = default)
{
    var trace = new List<string>();

    // Step 1: QueryAgent — RAG检索 + LLM生成
    trace.Add("QueryAgent: executing RAG pipeline");
    var request = new RagQueryRequest { Question = question };
    var response = await queryService.QueryAsync(request, ct);
    trace.Add($"QueryAgent: answer generated (confidence={response.AnswerConfidence:P0}, hallucination={response.IsHallucination})");

    // Step 2: EvalAgent — 幻觉检测（LLM self-verification）
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

**EvalAgent 实现：HallucinationGuardService**

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

**执行流程**：
```
User Question
    ↓
QueryAgent:
  - Embedding → Vector Search → LLM Generate
  - 返回: Answer + Sources + Confidence
    ↓
EvalAgent (HallucinationGuardService):
  - LLM作为fact-checker
  - System Prompt: "每个claim是否有Context支持？"
  - 返回: true/false
    ↓
OrchestrationResult:
  - Answer
  - IsEvaluated = true
  - EvaluationSummary = "answer is grounded ✓"
  - AgentTrace = ["QueryAgent: ...", "EvalAgent: ..."]
```

**优势**：
- ✅ **可测试** — 每个Agent独立单元测试
- ✅ **可调试** — 固定顺序，断点调试友好
- ✅ **可观测** — `AgentTrace` 完整记录每步
- ✅ **可靠** — 确定性执行，无LLM随机性

---

## 实战示例：双层幻觉防御

VedaAide 的幻觉检测是**两层架构**（防御纵深）：

### Layer 1: Embedding Cosine Similarity （快速筛选）

```csharp
// QueryService.cs
private async Task<bool> CheckHallucination(string answer, IReadOnlyList<SourceReference> sources, CancellationToken ct)
{
    if (sources.Count == 0) return true;  // 无来源 → 幻觉

    // 向量化答案
    var answerEmbedding = await embeddingService.GenerateEmbeddingAsync(answer, ct);

    // 计算与所有源片段的余弦相似度
    var maxSimilarity = 0f;
    foreach (var source in sources)
    {
        if (source.ChunkEmbedding is null) continue;
        var sim = VectorMath.CosineSimilarity(answerEmbedding, source.ChunkEmbedding);
        if (sim > maxSimilarity) maxSimilarity = sim;
    }

    // 低于阈值 → 幻觉
    return maxSimilarity < options.Value.HallucinationSimilarityThreshold;  // default 0.6
}
```

### Layer 2: LLM Self-Verification （精确验证）

仅当 Layer 1 通过 + `EnableSelfCheckGuard=true` 时调用：

```csharp
// 在 QueryService 中
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

**效果**：
- Layer 1 阻止明显偏离的答案（快速、低成本）
- Layer 2 逐句审核细节准确性（严格、高质量）
- 双层结合：速度 + 质量平衡

---

## Agent 编排的关键设计模式

### 1. Plugin Isolation（请求级隔离）

```csharp
var agentKernel = kernel.Clone();  // ✅ 每个请求独立的kernel实例
agentKernel.Plugins.AddFromObject(...);
```

**为什么**：避免并发请求的plugin污染，确保线程安全。

---

### 2. Reason-Act-Observe Loop（推理-行动-观察循环）

```
LLM Reasoning: "需要检索文档"
    ↓
Action: call search_knowledge_base(...)
    ↓
Observation: 返回检索结果
    ↓
LLM Reasoning: "结果不够，换个关键词再试"
    ↓
Action: call search_knowledge_base(...)
    ↓
...
```

这就是 **IRCoT (Interleaved Retrieval + Chain-of-Thought)** 的本质。

---

### 3. SRP (Single Responsibility Principle)

VedaAide 中每个Agent职责单一：

| Agent | 职责 | 实现 |
|-------|------|------|
| **QueryAgent** | 检索 + 生成答案 | `QueryService.QueryAsync()` |
| **EvalAgent** | 验证答案质量 | `HallucinationGuardService.VerifyAsync()` |
| **DocumentAgent** | 文档摄取 | `DocumentIngestor.IngestAsync()` |

**好处**：易测试、易维护、易扩展。

---

### 4. Observable Agent Chain（可观测链路）

```csharp
public class OrchestrationResult
{
    public string Answer { get; init; } = string.Empty;
    public bool IsEvaluated { get; init; }
    public string? EvaluationSummary { get; init; }
    public IReadOnlyList<string> AgentTrace { get; init; } = Array.Empty<string>();
    //                                        ^^^^^^^^^^^ 完整执行轨迹
}
```

**AgentTrace 示例**：
```
QueryAgent: executing RAG pipeline
QueryAgent: answer generated (confidence=87%, hallucination=False)
EvalAgent: answer is grounded in source documents ✓
```

用于调试、监控、审计。

---

## 生产环境的架构选择

实际项目中，我会**结合两种模式**：

```
外层：手动编排（OrchestrationService）
  ├─ QueryAgent（内层：LLM自主推理 with ChatCompletionAgent）
  ├─ EvalAgent（确定性：直接调用 HallucinationGuardService）
  └─ AuditAgent（确定性：记录日志 + 写入数据库）
```

**原则**：
- **需要LLM动态推理** → 用 `ChatCompletionAgent` + `FunctionChoiceBehavior.Auto()`
- **确定性流程** → 手动编排
- **生产环境必加** → 全链路trace + LLM验证层

---

## 总结

在 VedaAide.NET 项目中，我通过以下方式利用 Semantic Kernel 实现任务自动化：

1. **Plugin封装** — 将复杂的RAG检索封装为 `[KernelFunction]`，让LLM能直接调用
2. **Auto Function Calling** — `FunctionChoiceBehavior.Auto()` 实现 IRCoT，无需硬编码检索时机
3. **Agent链编排** — QueryAgent → EvalAgent 实现双层幻觉防御
4. **Plugin隔离** — `kernel.Clone()` 确保请求级安全
5. **可观测性** — `AgentTrace` 记录完整决策路径，便于调试和审计

这种架构既保证了**灵活性**（LLM自主推理），又确保了**可控性**（确定性流水线），在生产环境经过验证。

---

## 代码位置

- **LLM驱动Agent**: `src/Veda.Agents/LlmOrchestrationService.cs`
- **手动编排Agent**: `src/Veda.Agents/Orchestration/OrchestrationService.cs`
- **KernelPlugin**: `src/Veda.Agents/VedaKernelPlugin.cs`
- **幻觉检测**: `src/Veda.Services/HallucinationGuardService.cs`
- **RAG核心**: `src/Veda.Services/QueryService.cs`
