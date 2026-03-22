# CoT 与 IRCoT：提示技巧与检索增强推理

## CoT（Chain-of-Thought）

**本质**：Prompt 工程技巧，与 Agent 框架完全无关。

在 System Prompt 或 User Message 中要求模型"先推理，再得出结论"：

```
请按以下步骤作答：
1. 从 Context 中找出与问题直接相关的信息片段。
2. 分析这些信息，逐步推导出答案。
3. 给出最终结论。
```

**效果**：迫使 LLM 在输出最终答案前先展开推理链，相当于扩展了模型的"工作记忆"。
尤其适用于：数值计算、时间推断（"上上周三是几号？"）、多跳逻辑推理。

**在 VedaAide 中的实现**：`Veda.Prompts/ChainOfThoughtStrategy.cs` → `IChainOfThoughtStrategy.Enhance(question, context)` 注入 `QueryService`。

## IRCoT（Interleaved Retrieval with Chain-of-Thought）

论文：*"Interleaving Retrieval with Chain-of-Thought Reasoning"*（Trivedi et al., 2022）

**核心思想**：检索与推理**交替**进行，而不是"先检索一次，再一口气推理"。

```
普通 RAG:
  问题 → 一次检索 → 拿到所有上下文 → 一次推理 → 答案

IRCoT:
  问题 → 推理("需要先找Alice的老板") → 检索("Alice 老板") → 得知"Bob"
       → 推理("找Bob的出生城市")   → 检索("Bob 出生城市") → 答案
```

对跨文档多跳推理（如"Alice 的老板的出生城市"）效果显著。

## CoT vs IRCoT vs Agent 的关系

| 技术 | 是否需要循环 | 是否需要 Agent 框架 | 适用场景 |
|---|---|---|---|
| CoT | 否 | 否 | 单文档、逻辑推断、计算题 |
| 手写 IRCoT | 是（固定 N 次循环） | 否 | 已知需要 N 跳检索的问题 |
| Agent + IRCoT | 是（LLM 自主控制次数） | 是 | 不确定需要几跳的复杂问题 |

CoT 和 Agent 是**正交**的两个维度：
- CoT 是"如何推理"的问题（Prompt 工程）
- Agent 是"由谁决定行动"的问题（控制权在代码 vs LLM）

IRCoT 需要循环（手写固定循环也能做基础版），但要实现"LLM 自主控制检索次数"，必须依赖 Agent 框架。

## VedaAide 的演进路径

```
Phase 1-3 (当前):
  问题 → 一次检索 → LLM → 答案

Phase 4 (+CoT，已完成):
  问题 → 一次检索 → CoT Prompt → LLM推理链 → 答案

Phase 4.5 (IRCoT，待实现):
  方案A（手写循环）: 问题 → LLM生成子查询 → 检索 → 继续推理 → 检索 → 答案（固定轮数）
  方案B（真Agent）:  ChatCompletionAgent + VectorSearchPlugin → LLM自主决定检索次数
```

## 为什么 CoT 先实现、IRCoT 后实现

- CoT：2-3 行 Prompt 改动，零架构成本，立即有收益（遵循 YAGNI）
- IRCoT 手写版：需要拆分 `QueryService` 为多轮循环，增加代码复杂度
- IRCoT Agent 版：需要 `VectorSearchTool` 注册为 SK Plugin + `ChatCompletionAgent` 循环，是 Phase 4.5 的核心架构升级
