> **查看图表说明：** 浏览器安装 [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) 扩展；VS Code 安装 [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) 插件。

> English version: [03-query-flow.en.md](03-query-flow.en.md)

# 03 — Query 数据流

> 用户提问后，VedaAide 如何检索知识库、生成回答，并验证回答质量。

---

## 1. Query 整体流程图

```plantuml
@startuml query-overview
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12
skinparam activityBackgroundColor #E8F4FD
skinparam activityBorderColor #1976D2

title VedaAide — Query 整体流程（QueryService.QueryAsync）

|用户|
start
:提交问题 question;
note right
  三种入口：
  POST /api/query         (同步 JSON)
  GET  /api/querystream   (SSE 流式)
  POST /graphql           (GraphQL)
  MCP  search_knowledge_base
  IRCoT Agent Loop
end note

|QueryService|
:语义增强\nSemanticEnhancer.ExpandQueryAsync\n将缩写/自定义词汇扩展为规范同义词;
note right
  例："ML 会议" → "ML 会议 machine learning meeting"
  无词库时透传（NoOpSemanticEnhancer）
end note

|EmbeddingService|
:生成 queryEmbedding\n(Azure OpenAI 或 Ollama);

|QueryService|
:查语义缓存\nSemanticCache.GetAsync(queryEmbedding);
if (缓存命中？) then (命中\n相似度 ≥ 0.95)
  :返回缓存答案\n(AnswerConfidence=1, IsHallucination=false);
  stop
else (未命中)
endif

:candidateTopK = TopK × RerankCandidatesMultiplier;

if (HybridRetrievalEnabled？) then (是)
  |HybridRetriever|
  :向量检索通道\nSearchAsync(queryEmbedding, candidateTopK);
  :关键词检索通道\nSearchByKeywordsAsync(query, candidateTopK);
  note right
    两个通道顺序执行（SQLite DbContext 不支持并发）
  end note
  :融合排序\nRRF 或 WeightedSum;
  :返回 candidates;
else (否，仅向量检索)
  |IVectorStore|
  :SearchAsync(queryEmbedding, candidateTopK);
  :返回 candidates;
endif

|QueryService|
if (candidates 为空？) then (是)
  :返回"没有相关记录";
  stop
endif

:轻量 Rerank\n70% 向量相似度 + 30% 关键词覆盖率\n取前 TopK 个;

if (request.UserId 非空？) then (是)
  |FeedbackBoostService|
  :查用户历史反馈\nGetBoostFactorAsync(userId, chunkId);
  :按反馈权重重排\n(Accept +0.2 / Reject -0.15，上限 2.0 下限 0.3);
endif

|ContextWindowBuilder|
:按 Token 预算裁剪\n默认 maxTokens=3000\n(3 字符/token 估算)\n按相似度降序贪心选取;

|QueryService|
:BuildContext\n拼接上下文字符串\n[1] Source: xxx\n内容…;

:加载 System Prompt\n优先从 DB 读取 "rag-system" 模板\nfallback 到硬编码默认值;

if (StructuredOutput 模式？) then (是)
  :BuildStructuredPrompt\n要求 LLM 返回 JSON\n{type, summary, evidence,\nconfidence, uncertaintyNote};
else (否)
  |ChainOfThoughtStrategy|
  :Enhance(question, context)\n注入 CoT 步骤引导\n"1.找相关片段 2.逐步推导 3.给结论";
endif

|LlmRouter → IChatService|
:选择 LLM\n  Simple → GPT-4o-mini / Ollama qwen3\n  Advanced → DeepSeek（失败时降级 Simple）;
:CompleteAsync(systemPrompt, userMessage);
:得到 answer;

if (StructuredOutput？) then (是)
  |StructuredOutputParser|
  :TryParse(answer)\n解析 JSON → StructuredFinding\n失败时安全降级为 null;
endif

|EmbeddingService|
:生成 answerEmbedding;

|QueryService|
:防幻觉第一层\nanswerEmbedding 查向量库 TopK=1\nmaxSimilarity < HallucinationThreshold(0.3)\n→ 标记 IsHallucination=true;

if (!isHallucination && EnableSelfCheckGuard？) then (是)
  |HallucinationGuardService|
  :防幻觉第二层\n调用 LLM 自我校验\n"Answer 是否完全由 Context 支撑？"\ntrue/false;
  if (selfCheck = false？) then (是)
    :isHallucination = true;
  endif
endif

|QueryService|
if (!isHallucination) then
  :SemanticCache.SetAsync\n写入语义缓存;
endif

:返回 RagQueryResponse\n(answer, sources, isHallucination,\nanswerConfidence, structuredOutput);

|用户|
stop

@enduml
```

---

## 2. 混合检索融合细图

```plantuml
@startuml hybrid-retrieval
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 混合检索双通道 + RRF 融合（HybridRetriever）

participant "QueryService" as Q
participant "HybridRetriever" as HR
participant "IVectorStore" as VS

Q -> HR : RetrieveAsync(query, queryEmbedding, topK, options)

HR -> VS : SearchAsync(queryEmbedding, candidateK=topK×4)\n【向量通道】余弦相似度排序
VS --> HR : vectorResults [(chunk, similarity)]

HR -> VS : SearchByKeywordsAsync(query, candidateK)\n【关键词通道】BM25 平替\n(SQLite: LIKE 内存过滤 / CosmosDB: CONTAINS)
VS --> HR : keywordResults [(chunk, score)]

alt FusionStrategy == Rrf（默认）
  HR -> HR : RRF 融合\n两个通道分别按排名计算\nscore += 1 / (60 + rank)\n对同一 chunk 的分数累加
else FusionStrategy == WeightedSum
  HR -> HR : 加权融合\nscore = VectorWeight×similarity\n       + KeywordWeight×keywordScore\n(默认 0.7 + 0.3)
end

HR -> HR : 按融合分数降序排列\n取前 topK 个

HR --> Q : fusedResults [(chunk, score)]

note right of HR
  RRF 公式：score = Σ 1/(k+rank)，k=60
  优势：
  · 不需要分数归一化
  · 天然抑制头部集中（长尾效果好）
  · 对两个通道质量不均匀时鲁棒
end note

@enduml
```

---

## 3. Rerank + Feedback Boost 细图

```plantuml
@startuml rerank-boost
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title Rerank → Feedback Boost 两阶段重排

rectangle "HybridRetriever/VectorStore 输出" as Input #E8F4FD {
  rectangle "candidateTopK 个候选\n(TopK × RerankCandidatesMultiplier)" as Cands
}

rectangle "Rerank（QueryService 内）" as Rerank #E8F8E8 {
  note as RNote
    轻量混合重排（无需额外 LLM）：
    
    combined = 0.7 × vectorSimilarity
             + 0.3 × keywordOverlapRate
    
    keywordOverlapRate =
      问题关键词出现在 chunk 中的比例
    
    取 combined 最高的 TopK 个
  end note
}

rectangle "FeedbackBoostService（可选）" as Boost #FFF3CD {
  note as BNote
    无 UserId → 跳过

    有 UserId：
    boostFactor = 1.0
      + accepts × 0.2
      - rejects × 0.15
    clamp [0.3, 2.0]

    finalScore = rerankScore × boostFactor
    按 finalScore 重排
  end note
}

rectangle "ContextWindowBuilder 输入" as Output #F8E8FF {
  rectangle "TopK 个最终候选" as Final
}

Input --> Rerank : candidateTopK 候选
Rerank --> Boost : TopK 候选（按 combined 排序）
Boost --> Output : TopK 候选（按 finalScore 排序）

@enduml
```

---

## 4. 防幻觉双层校验

```plantuml
@startuml hallucination-guard
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title 防幻觉双层校验机制

rectangle "LLM 生成答案 answer" as Answer #E8F4FD

rectangle "第一层：Embedding 相似度校验（必选）" as L1 #FFF0E8 {
  note as L1Note
    1. 对 answer 生成 answerEmbedding
    2. 用 answerEmbedding 查向量库 TopK=1
    3. maxSimilarity = 最高相似度
    4. if maxSimilarity < HallucinationSimilarityThreshold(0.3)
          IsHallucination = true

    原理：
    如果 LLM 的回答与知识库内容
    完全不相关（余弦距离很远）
    → 大概率是凭空捏造
  end note
}

rectangle "第二层：LLM 自我校验（可选，EnableSelfCheckGuard）" as L2 #FFE8F0 {
  note as L2Note
    仅在第一层通过 && EnableSelfCheckGuard=true 时执行

    System Prompt：
    "你是严格的事实核查助手。
    判断 Answer 是否完全由 Context 支撑。
    仅回答 true 或 false。"

    User Prompt：
    Context: {检索到的原始文档}
    Answer to verify: {LLM 回答}

    if response.StartsWith("false")
       IsHallucination = true

    代价：额外消耗 1 次 LLM 调用
    建议仅在高置信度场景开启
  end note
}

rectangle "结果" as Result #E8F8E8 {
  note as RNote
    IsHallucination = false
    → 写入语义缓存
    → 前端正常展示

    IsHallucination = true
    → 不写缓存
    → 前端可显示警告标识
    → 答案仍会返回（供参考）
  end note
}

Answer --> L1
L1 --> L2 : 如果第一层通过
L1 --> Result : 如果第一层失败
L2 --> Result

@enduml
```

---

## 5. IRCoT Agent 模式（Phase 4）

```plantuml
@startuml ircot-agent
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title IRCoT Agent 编排（LlmOrchestrationService）

participant "用户" as User
participant "LlmOrchestrationService" as Orch
participant "ChatCompletionAgent\n(Semantic Kernel)" as Agent
participant "VedaKernelPlugin\nsearch_knowledge_base" as Plugin
participant "IVectorStore" as VS
participant "Azure OpenAI / Ollama" as LLM

User -> Orch : RunQueryFlowAsync(question)
Orch -> Agent : InvokeAsync(question)

loop Reason-Act-Observe 循环（LLM 自主决策）
  Agent -> LLM : 推理：当前知道什么？需要查什么？
  LLM --> Agent : FunctionChoiceBehavior.Auto()\n决定调用 search_knowledge_base

  Agent -> Plugin : search_knowledge_base(query, topK)
  Plugin -> VS : SearchAsync(embedding, topK)
  VS --> Plugin : 相关 chunks
  Plugin --> Agent : 格式化文本片段（Observe）

  LLM --> Agent : 基于新信息继续推理\n（决定是否再次检索）
end

Agent --> Orch : 最终综合答案
Orch -> Orch : 提取 tool-call 次数 / sources
Orch -> Orch : HallucinationGuard.VerifyAsync（可选）
Orch --> User : OrchestrationResult\n(answer, agentTrace, sources)

note right of Agent
  QueryAgentInstructions：
  1. ALWAYS call search_knowledge_base first
  2. If results insufficient, refine & retry
  3. Synthesize retrieved info into answer
  4. If nothing found, say so explicitly
end note

note right of LLM
  每次循环 = 1 次 LLM 推理
  可能多次调用 search_knowledge_base
  = IRCoT（Interleaved Retrieval + CoT）
end note

@enduml
```

---

## 6. 流式问答（SSE）

```plantuml
@startuml sse-stream
skinparam backgroundColor #FAFAFA
skinparam defaultFontSize 12

title SSE 流式问答（QueryStreamController）

participant "前端 EventSource" as FE
participant "QueryStreamController" as Ctrl
participant "QueryService\nQueryStreamAsync" as Svc
participant "LLM" as LLM

FE -> Ctrl : GET /api/querystream?question=...
Ctrl -> Ctrl : Response.ContentType = text/event-stream

Ctrl -> Svc : QueryStreamAsync(request)

Svc -> Svc : [同步步骤] 语义增强 + Embedding\n+ 混合检索 + Rerank + BuildContext

Svc --> Ctrl : yield RagStreamChunk { type="sources", sources=[...] }
Ctrl --> FE : data: {"type":"sources","sources":[...]}

Svc -> LLM : CompleteStreamAsync(systemPrompt, userMessage)
loop 逐 token 流式输出
  LLM --> Svc : token
  Svc --> Ctrl : yield RagStreamChunk { type="token", content=token }
  Ctrl --> FE : data: {"type":"token","content":"..."}
end

Svc -> Svc : 防幻觉检测（同非流式）

Svc --> Ctrl : yield RagStreamChunk { type="done", isHallucination=... }
Ctrl --> FE : data: {"type":"done","isHallucination":false}

FE -> FE : EventSource 关闭

note right of Ctrl
  先推送 sources 让前端立即展示来源
  再逐 token 推送让回答"打字机"效果
  最后 done 事件含幻觉标志
end note

@enduml
```

---

## 7. 关键代码位置速查

| 步骤 | 类 / 文件 | 方法 |
|------|-----------|------|
| HTTP 入口（同步） | `QueryController` | `Query()` |
| HTTP 入口（流式） | `QueryStreamController` | `Stream()` |
| GraphQL 入口 | `Veda.Api/GraphQL/Query` | `Query()` |
| MCP 入口 | `KnowledgeBaseTools` | `SearchKnowledgeBase()` |
| Agent 编排入口 | `LlmOrchestrationService` | `RunQueryFlowAsync()` |
| 主查询流程 | `QueryService` | `QueryAsync()` |
| 流式查询流程 | `QueryService` | `QueryStreamAsync()` |
| 查询扩展 | `PersonalVocabularyEnhancer` | `ExpandQueryAsync()` |
| 语义缓存命中 | `SqliteSemanticCache` / `CosmosDbSemanticCache` | `GetAsync()` |
| 混合检索 | `HybridRetriever` | `RetrieveAsync()` |
| RRF 融合 | `HybridRetriever` | `FuseRrf()` |
| 加权融合 | `HybridRetriever` | `FuseWeighted()` |
| 轻量 Rerank | `QueryService` | `Rerank()` |
| 反馈 Boost | `FeedbackBoostService` | `ApplyBoostAsync()` |
| Token 预算裁剪 | `ContextWindowBuilder` | `Build()` |
| CoT 注入 | `ChainOfThoughtStrategy` | `Enhance()` |
| 结构化 Prompt | `QueryService` | `BuildStructuredPrompt()` |
| LLM 路由 | `LlmRouterService` | `Resolve()` |
| Chat 适配器 | `OllamaChatService` | `CompleteAsync()` / `CompleteStreamAsync()` |
| 结构化输出解析 | `StructuredOutputParser` | `TryParse()` |
| 防幻觉第一层 | `QueryService` | `QueryAsync()` 内 |
| 防幻觉第二层 | `HallucinationGuardService` | `VerifyAsync()` |
| 语义缓存写入 | `SqliteSemanticCache` / `CosmosDbSemanticCache` | `SetAsync()` |
| IRCoT Agent | `LlmOrchestrationService` | `RunQueryFlowAsync()` |
| SK Plugin | `VedaKernelPlugin` | `SearchKnowledgeBaseAsync()` |
