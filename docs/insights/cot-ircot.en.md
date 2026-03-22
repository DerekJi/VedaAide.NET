# CoT and IRCoT: Prompt Reasoning Techniques and Retrieval-Augmented Reasoning

**Phase**: Phase 4

> 中文版见 [cot-ircot.cn.md](cot-ircot.cn.md)

---

## CoT (Chain-of-Thought)

**Essence**: A prompt engineering technique, completely unrelated to Agent frameworks.

Instruct the model to "reason first, then conclude" in the System Prompt or User Message:

```
Please answer following these steps:
1. Find information fragments directly relevant to the question in the Context.
2. Analyze this information and gradually derive the answer step by step.
3. State your final conclusion.
```

**Effect**: Forces the LLM to lay out a reasoning chain before outputting the final answer — effectively expanding the model's "working memory".
Especially effective for: numerical calculations, temporal reasoning ("what day was two Wednesdays ago?"), multi-hop logical deduction.

**Implementation in VedaAide**: `Veda.Prompts/ChainOfThoughtStrategy.cs` → `IChainOfThoughtStrategy.Enhance(question, context)` injected into `QueryService`.

## IRCoT (Interleaved Retrieval with Chain-of-Thought)

Paper: *"Interleaving Retrieval with Chain-of-Thought Reasoning"* (Trivedi et al., 2022)

**Core idea**: Retrieval and reasoning **interleave** — instead of "retrieve once, then reason all at once".

```
Standard RAG:
  Question → one retrieval → get all context → one reasoning pass → Answer

IRCoT:
  Question → reason("need to find Alice's boss first") → retrieve("Alice boss") → learn "Bob"
           → reason("find Bob's birth city")            → retrieve("Bob birth city") → Answer
```

Significantly effective for cross-document multi-hop reasoning (e.g., "what is Alice's boss's birth city?").

## Relationship Between CoT, IRCoT, and Agents

| Technique | Requires Loop | Requires Agent Framework | Best For |
|---|---|---|---|
| CoT | No | No | Single-document, logical deduction, calculation |
| Handwritten IRCoT | Yes (fixed N iterations) | No | Problems known to require N retrieval hops |
| Agent + IRCoT | Yes (LLM controls iteration count) | Yes | Complex problems where number of hops is unknown |

CoT and Agent are **orthogonal** dimensions:
- CoT is about "how to reason" (prompt engineering)
- Agent is about "who decides actions" (control rests in code vs. LLM)

IRCoT requires a loop (a basic version can be handwritten with a fixed loop), but for "LLM autonomously controls how many retrieval calls to make", an Agent framework is required.

## VedaAide's Evolution Path

```
Phase 1-3:
  Question → one retrieval → LLM → Answer

Phase 4 (+CoT, done):
  Question → one retrieval → CoT Prompt → LLM reasoning chain → Answer

Phase 4.5 (IRCoT, done via LlmOrchestrationService):
  ChatCompletionAgent + VectorSearchPlugin → LLM autonomously decides retrieval count
```

## Why CoT Was Implemented First, IRCoT Second

- CoT: 2–3 lines of prompt change, zero architectural cost, immediate payoff (follows YAGNI)
- Handwritten IRCoT: requires splitting `QueryService` into a multi-round loop, increasing code complexity
- IRCoT Agent version: requires `VectorSearchTool` registered as an SK Plugin + `ChatCompletionAgent` loop — the core architectural upgrade of Phase 4.5, now implemented via `LlmOrchestrationService`
