# RAG Prompt Reasoning Boundary: Strict Mode vs. Reasonable Inference

**Project**: VedaAide.NET  
**Files**: `src/Veda.Services/QueryService.cs` → `BuildSystemPrompt()`  
**Phase**: Phase 3

> 中文版见 [rag-prompt-boundary.cn.md](rag-prompt-boundary.cn.md)

---

## Background

The classic RAG approach explicitly instructs in the System Prompt:
> "Only answer based on the document content. Otherwise, reply with 'no relevant information'."

The intent is to prevent LLM hallucination. However, in a personal assistant scenario, a practical problem emerged:

The user recorded two notes:
- `[2026-03-20] My family eats a handful of greens every evening`
- `[2026-03-20] Bought three handfuls of greens yesterday at noon`

When the user asks "If I don't buy greens tomorrow, will we still have any for tomorrow evening?", the strict mode answer is:
> "I don't have enough information in the provided documents."

This is technically correct (the documents don't explicitly state "will there be greens tomorrow"), but completely useless to the user — the answer is right there in the combination of the two notes.

---

## Core Tension

**Implicit assumption of strict mode**: the knowledge base content is complete enough that questions can be answered by directly finding information in the documents.

**Reality of personal assistant scenarios**: users record **fragmented facts**, not "complete Q&A pairs". The knowledge base is never complete — its value lies in supporting inference, not acting as an encyclopedia to look things up directly.

More importantly: **strict prohibition of reasoning doesn't actually reduce hallucination**. The LLM will hallucinate when it wants to — wrapping it in "I don't know" just hides the real risk. The user can't tell whether the model "genuinely lacks relevant material" or "has material but refuses to reason with it".

---

## Solution: Draw the Line at Reasoning Evidence, Not "Ban All Reasoning"

| Situation | Treatment |
|---|---|
| Answer can be directly read from context | Answer directly |
| Answer requires combining multiple context fragments + common sense | **Allowed**, but explain the reasoning basis in the answer |
| Answer requires world knowledge outside the context | Refuse, or explicitly flag "beyond the scope of notes" |
| Context has no relevant content at all | Explicitly refuse |

Reflected in the rules inside `BuildSystemPrompt()`:

```csharp
// Rule 3: partially relevant → infer and explain reasoning
// Rule 4: completely irrelevant → explicitly refuse
```

---

## The Real Boundary for Hallucination Risk

The hallucination that truly needs to be guarded against is **situation three**: the LLM fabricates a plausible-sounding answer using its own training data, unrelated to the user's notes.

The existing two-layer hallucination detection system targets exactly this type:
1. **Answer Embedding Check**: embed the generated answer, compare with the vector store, flag if similarity is too low
2. **Self-Check Guard (optional)**: call LLM again to audit whether the answer has documentary support

"Yesterday bought three handfuls, eat one per day, therefore tomorrow there will still be some" — this kind of cross-fragment combined inference is **not** in the hallucination risk zone. It is valuable reasoning that should be permitted.

---

## Difference from MCP / Agent Scenarios

This design decision is highly **context-sensitive**:

### Current scenario: conversational UI directly facing the user
- Users expect valuable answers, not raw chunks
- **Allowing inference** is the right choice
- System Prompt should be friendly and encourage inference

### Future scenario: as an MCP Tool called by an Agent
- The calling Agent has its own reasoning layer, expecting the tool to return **trustworthy facts**, not already-processed inferences
- The RAG tool should **revert to strict mode**: only return retrieved chunks + similarity scores, without LLM generation
- A tool that reasons too much interferes with the Agent's decision chain, causing "double reasoning" contamination
