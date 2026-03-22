# Dual-Layer Hallucination Detection

**Project**: VedaAide.NET  
**Files**: `src/Veda.Services/QueryService.cs`, `src/Veda.Services/HallucinationGuardService.cs`  
**Phase**: Phase 2

> 中文版见 [anti-hallucination.cn.md](anti-hallucination.cn.md)

---

## Defining Hallucination in the RAG Context

In RAG, hallucination specifically means: **the LLM generated content unrelated to the retrieved context** — the model used its own training data to fabricate a plausible-sounding answer that isn't in the knowledge base.

This is more subtle than general "LLM hallucination" (making things up from nothing), because the answer may be factually correct in the real world, but doesn't come from the user's knowledge base. In compliance-sensitive domains (medical, legal, finance), this is unacceptable.

---

## Layer 1: Answer Embedding Check (Vector Similarity)

**Logic**: Embed the full LLM-generated answer, then search the vector store for the most similar chunk.

```csharp
// QueryService.cs
var answerEmbedding = await embeddingService.GenerateEmbeddingAsync(answer, ct);
var answerCheck = await vectorStore.SearchAsync(answerEmbedding, topK: 1, minSimilarity: 0f, ct: ct);
var maxAnswerSimilarity = answerCheck.Count > 0 ? answerCheck[0].Similarity : 0f;
var isHallucination = maxAnswerSimilarity < options.Value.HallucinationSimilarityThreshold; // default 0.3
```

**Principle**: If the answer is genuinely based on knowledge base content, its semantic vector should be closely aligned with vectors of chunks in the store. If similarity < 0.3, the answer's semantics are "drifting" outside the knowledge base.

**Pros**: Zero extra LLM calls, extremely low cost (only one extra Embedding + vector query).  
**Limitation**: Statistical judgment, not logical. High similarity doesn't guarantee no hallucination — it only lowers the risk.

---

## Layer 2: LLM Self-Check

**Logic**: Send the original context and LLM answer together to the LLM, asking it to determine whether the answer is fully grounded.

```csharp
// HallucinationGuardService.cs — System Prompt requires only true/false
var response = await chatService.CompleteAsync(SystemPrompt, userMessage, ct);
return response.Trim().StartsWith("true", StringComparison.OrdinalIgnoreCase);
```

**Pros**: Semantic-level judgment — can identify hallucinations where "the answer logic is correct but the source is wrong".  
**Cost**: One extra full LLM call (slow and expensive). Disabled by default via `EnableSelfCheckGuard: false`, enable on demand.

---

## How the Two Layers Complement Each Other

| | Layer 1 (Embedding Check) | Layer 2 (Self-Check) |
|---|---|---|
| Cost | Very low (1 Embedding call) | High (1 full LLM call) |
| Speed | Milliseconds | Seconds |
| Judgment method | Statistical (vector distance) | Semantic (LLM reasoning) |
| Default state | Always on | Off, configurable |
| Best for | Production fast filtering | High-compliance scenarios |

Design principle mirrors dual-layer deduplication: **use cheap statistical methods first for quick filtering, only enable expensive LLM judgment when necessary**.

---

## How Results Are Handled

The hallucination detection result does not block the answer — it is **flagged and passed through to the frontend**:

```csharp
return new RagQueryResponse
{
    Answer = answer,
    IsHallucination = isHallucination,  // frontend shows ⚠ warning
    AnswerConfidence = results.Max(r => r.Similarity)
};
```

This design is intentional: blocking would prevent the user from seeing the answer at all, but sometimes a "potentially hallucinated answer" still has reference value. **Let the user decide**, rather than having the system decide "this answer shouldn't be shown to you".
