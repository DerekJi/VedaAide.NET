> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[03-query-flow.cn.md](03-query-flow.cn.md)

# 03 — Query Pipeline

> How VedaAide retrieves knowledge, generates an answer, and validates answer quality after a user asks a question.

---

## 1. Query Pipeline Overview

```plantuml
@startuml query-overview
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam activityBackgroundColor #E8F4FD
skinparam activityBorderColor #1976D2

title VedaAide — Query Pipeline (QueryService.QueryAsync)

|User|
start
:Submit question;
note right
  Five entry points:
  POST /api/query         (sync JSON)
  GET  /api/querystream   (SSE streaming)
  POST /graphql           (GraphQL)
  MCP  search_knowledge_base
  IRCoT Agent Loop
end note

|QueryService|
:Semantic expansion\nSemanticEnhancer.ExpandQueryAsync\nExpand abbreviations/custom terms to canonical synonyms;
note right
  e.g. "ML meeting" → "ML meeting machine learning"
  Pass-through when no vocabulary configured (NoOpSemanticEnhancer)
end note

|EmbeddingService|
:Generate queryEmbedding\n(Azure OpenAI or Ollama);

|QueryService|
:Check semantic cache\nSemanticCache.GetAsync(queryEmbedding);
if (Cache hit?) then (hit\nsimilarity ≥ 0.95)
  :Return cached answer\n(AnswerConfidence=1, IsHallucination=false);
  stop
else (miss)
endif

:candidateTopK = TopK × RerankCandidatesMultiplier;

if (HybridRetrievalEnabled?) then (yes)
  |HybridRetriever|
  :Vector retrieval channel\nSearchAsync(queryEmbedding, candidateTopK);
  :Keyword retrieval channel\nSearchByKeywordsAsync(query, candidateTopK);
  note right
    Two channels run sequentially
    (SQLite DbContext does not support concurrency)
  end note
  :Fusion ranking\nRRF or WeightedSum;
  :Return candidates;
else (no — vector only)
  |IVectorStore|
  :SearchAsync(queryEmbedding, candidateTopK);
  :Return candidates;
endif

|QueryService|
if (Candidates empty?) then (yes)
  :Return "No relevant records found";
  stop
endif

:Lightweight Rerank\n70% vector similarity + 30% keyword coverage\nKeep top TopK;

if (request.UserId not null?) then (yes)
  |FeedbackBoostService|
  :Query user history feedback\nGetBoostFactorAsync(userId, chunkId);
  :Re-rank by feedback weight\n(Accept +0.2 / Reject -0.15, clamp [0.3, 2.0]);
endif

|ContextWindowBuilder|
:Token budget trimming\ndefault maxTokens=3000\n(~3 chars/token estimate)\nGreedy selection in descending similarity order;

|QueryService|
:BuildContext\nConcatenate context string\n[1] Source: xxx\ncontent...;

:Load System Prompt\nRead "rag-system" template from DB first\nFallback to hardcoded default;

if (StructuredOutput mode?) then (yes)
  :BuildStructuredPrompt\nAsk LLM to return JSON\n{type, summary, evidence,\nconfidence, uncertaintyNote};
else (no)
  |ChainOfThoughtStrategy|
  :Enhance(question, context)\nInject CoT steps\n"1.Find relevant fragments 2.Reason step by step 3.Give conclusion";
endif

|LlmRouter → IChatService|
:Select LLM\n  Simple → GPT-4o-mini / Ollama qwen3\n  Advanced → DeepSeek (fallback to Simple on failure);
:CompleteAsync(systemPrompt, userMessage);
:Receive answer;

if (StructuredOutput?) then (yes)
  |StructuredOutputParser|
  :TryParse(answer)\nParse JSON → StructuredFinding\nFail safely to null;
endif

|EmbeddingService|
:Generate answerEmbedding;

|QueryService|
:Hallucination guard layer 1\nanswerEmbedding queries vector store TopK=1\nmaxSimilarity < HallucinationThreshold(0.3)\n→ mark IsHallucination=true;

if (!isHallucination && EnableSelfCheckGuard?) then (yes)
  |HallucinationGuardService|
  :Hallucination guard layer 2\nLLM self-check\n"Is Answer fully supported by Context?"\ntrue/false;
  if (selfCheck = false?) then (yes)
    :isHallucination = true;
  endif
endif

|QueryService|
if (!isHallucination) then
  :SemanticCache.SetAsync\nWrite to semantic cache;
endif

:Return RagQueryResponse\n(answer, sources, isHallucination,\nanswerConfidence, structuredOutput);

|User|
stop

@enduml
```

---

## 2. Hybrid Retrieval Fusion

```plantuml
@startuml hybrid-retrieval
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Hybrid Retrieval Dual Channel + RRF Fusion (HybridRetriever)

participant "QueryService" as Q
participant "HybridRetriever" as HR
participant "IVectorStore" as VS

Q -> HR : RetrieveAsync(query, queryEmbedding, topK, options)

HR -> VS : SearchAsync(queryEmbedding, candidateK=topK×4)\n[Vector channel] cosine similarity ranking
VS --> HR : vectorResults [(chunk, similarity)]

HR -> VS : SearchByKeywordsAsync(query, candidateK)\n[Keyword channel] BM25 substitute\n(SQLite: LIKE in-memory / CosmosDB: CONTAINS)
VS --> HR : keywordResults [(chunk, score)]

alt FusionStrategy == Rrf (default)
  HR -> HR : RRF fusion\nFor each channel, compute by rank:\nscore += 1 / (60 + rank)\nAccumulate scores for the same chunk
else FusionStrategy == WeightedSum
  HR -> HR : Weighted fusion\nscore = VectorWeight×similarity\n       + KeywordWeight×keywordScore\n(default 0.7 + 0.3)
end

HR -> HR : Sort by fusion score descending\nTake top topK

HR --> Q : fusedResults [(chunk, score)]

note right of HR
  RRF formula: score = Σ 1/(k+rank), k=60
  Advantages:
  · No score normalization needed
  · Naturally suppresses head concentration (good for long tail)
  · Robust to uneven quality between channels
end note

@enduml
```

---

## 3. Rerank + Feedback Boost

```plantuml
@startuml rerank-boost
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Rerank → Feedback Boost Two-Stage Re-ranking

rectangle "HybridRetriever/VectorStore output" as Input #E8F4FD {
  rectangle "candidateTopK candidates\n(TopK × RerankCandidatesMultiplier)" as Cands
}

rectangle "Rerank (inside QueryService)" as Rerank #E8F8E8 {
  note as RNote
    Lightweight hybrid re-ranking (no extra LLM needed):
    
    combined = 0.7 × vectorSimilarity
             + 0.3 × keywordOverlapRate
    
    keywordOverlapRate =
      proportion of query keywords present in chunk
    
    Keep top TopK by combined score
  end note
}

rectangle "FeedbackBoostService (optional)" as Boost #FFF3CD {
  note as BNote
    No UserId → skip

    With UserId:
    boostFactor = 1.0
      + accepts × 0.2
      - rejects × 0.15
    clamp [0.3, 2.0]

    finalScore = rerankScore × boostFactor
    Re-rank by finalScore
  end note
}

rectangle "ContextWindowBuilder input" as Output #F8E8FF {
  rectangle "TopK final candidates" as Final
}

Input --> Rerank : candidateTopK candidates
Rerank --> Boost : TopK candidates (sorted by combined)
Boost --> Output : TopK candidates (sorted by finalScore)

@enduml
```

---

## 4. Two-Layer Hallucination Guard

```plantuml
@startuml hallucination-guard
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Two-Layer Hallucination Guard Mechanism

rectangle "LLM-generated answer" as Answer #E8F4FD

rectangle "Layer 1: Embedding Similarity Check (mandatory)" as L1 #FFF0E8 {
  note as L1Note
    1. Generate answerEmbedding for the answer
    2. Query vector store with answerEmbedding TopK=1
    3. maxSimilarity = highest similarity score
    4. if maxSimilarity < HallucinationSimilarityThreshold(0.3)
          IsHallucination = true

    Rationale:
    If the LLM's answer is completely unrelated
    to knowledge base content (large cosine distance)
    → likely fabricated
  end note
}

rectangle "Layer 2: LLM Self-Check (optional, EnableSelfCheckGuard)" as L2 #FFE8F0 {
  note as L2Note
    Executes only when Layer 1 passes && EnableSelfCheckGuard=true

    System Prompt:
    "You are a strict fact-checking assistant.
    Determine whether the Answer is fully supported
    by the Context. Answer only true or false."

    User Prompt:
    Context: {retrieved source documents}
    Answer to verify: {LLM answer}

    if response.StartsWith("false")
       IsHallucination = true

    Cost: one extra LLM call
    Recommended only for high-confidence scenarios
  end note
}

rectangle "Result" as Result #E8F8E8 {
  note as RNote
    IsHallucination = false
    → Write to semantic cache
    → Display normally in UI

    IsHallucination = true
    → Do not cache
    → UI may show warning indicator
    → Answer still returned (for reference)
  end note
}

Answer --> L1
L1 --> L2 : if Layer 1 passes
L1 --> Result : if Layer 1 fails
L2 --> Result

@enduml
```

---

## 5. IRCoT Agent Mode (Phase 4)

```plantuml
@startuml ircot-agent
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title IRCoT Agent Orchestration (LlmOrchestrationService)

participant "User" as User
participant "LlmOrchestrationService" as Orch
participant "ChatCompletionAgent\n(Semantic Kernel)" as Agent
participant "VedaKernelPlugin\nsearch_knowledge_base" as Plugin
participant "IVectorStore" as VS
participant "Azure OpenAI / Ollama" as LLM

User -> Orch : RunQueryFlowAsync(question)
Orch -> Agent : InvokeAsync(question)

loop Reason-Act-Observe loop (LLM decides autonomously)
  Agent -> LLM : Reason: What do I know? What should I look up?
  LLM --> Agent : FunctionChoiceBehavior.Auto()\ndecide to call search_knowledge_base

  Agent -> Plugin : search_knowledge_base(query, topK)
  Plugin -> VS : SearchAsync(embedding, topK)
  VS --> Plugin : Relevant chunks
  Plugin --> Agent : Formatted text fragments (Observe)

  LLM --> Agent : Continue reasoning based on new information\n(decide whether to search again)
end

Agent --> Orch : Final synthesized answer
Orch -> Orch : Extract tool-call count / sources
Orch -> Orch : HallucinationGuard.VerifyAsync (optional)
Orch --> User : OrchestrationResult\n(answer, agentTrace, sources)

note right of Agent
  QueryAgentInstructions:
  1. ALWAYS call search_knowledge_base first
  2. If results insufficient, refine & retry
  3. Synthesize retrieved info into answer
  4. If nothing found, say so explicitly
end note

note right of LLM
  Model: gpt-4o-mini (Simple)
  or DeepSeek (Advanced)
  FunctionChoiceBehavior.Auto()
  ——LLM decides when and how many
    times to call tools
end note

@enduml
```
