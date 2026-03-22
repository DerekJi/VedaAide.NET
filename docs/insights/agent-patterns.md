# Agent 编排模式：确定性调用链 vs 真正的 LLM 驱动 Agent

## 当前实现（Phase 4 基础版）

`OrchestrationService` 是**硬编码的确定性调用链**，执行顺序由代码固定，不是 LLM 驱动的 Agent：

```
RunIngestFlowAsync:
  InferDocumentType(filename)       ← 字符串匹配，非 LLM
  → DocumentIngestService.IngestAsync()

RunQueryFlowAsync:
  QueryService.QueryAsync()
  → HallucinationGuardService.VerifyAsync()
  → 返回 OrchestrationResult（含 agentTrace 执行记录）
```

每个"Agent"都是对现有 Service 的简单包装，调用顺序不会根据问题性质动态调整。

## 真正的 Agentic Loop（Phase 4.5 目标）

真正的 Agent 需要三个要素：
1. **LLM 自主决定工具调用**：LLM 看到可用工具列表，自主选择调用哪个、调用几次
2. **迭代循环（Reason-Act-Observe）**：每次工具返回结果后，LLM 重新判断是否需要继续
3. **对话历史维护**：多轮对话中保持上下文状态

Semantic Kernel 实现路径（Phase 4.5）：
```csharp
var agent = new ChatCompletionAgent
{
    Kernel = kernel,
    Instructions = systemPrompt,
    Name = "QueryAgent"
};
kernel.Plugins.AddFromType<KnowledgeBasePlugin>();
// LLM 自主决定何时调用 search_knowledge_base，以及迭代几次
await foreach (var message in agent.InvokeAsync(thread))
{
    // 每轮都可能触发工具调用
}
```

## Agent 职责边界（正确的分类方式）

```
DocumentIngestAgent（内容处理层）
  ↑ 被委托
FileSystemIngestAgent（文件系统数据源）
BlobStorageIngestAgent（Azure Blob 数据源）
DatabaseIngestAgent（数据库数据源）  ← 未来扩展
```

设计中"DocumentAgent"存在命名歧义：
- 实现中是 `DocumentIngestAgent`（处理内容：分块、Embedding、去重、入库）
- 不是"文档来源 Agent"
- 来源 Agent（FileSystem/Blob）负责获取内容，委托 `DocumentIngestAgent` 处理，SRP 清晰

## EvalAgent 的两种执行时序

| 场景 | 时序 | 作用 |
|---|---|---|
| **实时问答**（Phase 4） | 单次 Query 后串行执行 | 对当前答案做即时幻觉校验 |
| **批量测试**（Phase 5 Test Harness） | 所有 Query 完成后统一执行 | 横向对比不同模型/Prompt 的评估得分 |

当前 `EvalAgent` = `HallucinationGuardService.VerifyAsync()` 的包装（向量相似度 + 可选 LLM 自我校验）。

Phase 5 升级为 `FaithfulnessScorer` + `AnswerRelevancyScorer` 并行运行，支持 A/B 测试报告。
