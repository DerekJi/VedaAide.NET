> **查看图表说明：** 浏览器安装 [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) 扩展；VS Code 安装 [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) 插件。

> English version: [05-concept-code-map.en.md](05-concept-code-map.en.md)

# 05 — RAG 概念与代码对照表

> 把 RAG 技术体系中常见的概念，逐一映射到 VedaAide 的具体实现位置。  
> 适合面试准备、新成员入门、或技术交流时快速定位代码。

---

## 概念对照总表

| RAG 概念 | 技术要点 | VedaAide 实现 | 代码位置 |
|---------|---------|--------------|---------|
| **Chunking（分块）** | 把长文档切成适合 Embedding 的短片段，控制 token 数量 | 滑动窗口，按 DocumentType 动态选择 TokenSize / OverlapTokens | `TextDocumentProcessor.Process()` · `ChunkingOptions.ForDocumentType()` |
| **Embedding（向量化）** | 用语言模型把文本映射为高维向量，同义文本向量相近 | 支持 Azure OpenAI `text-embedding-3-small`（1536维）和 Ollama `bge-m3`（1024维），通过配置切换 | `EmbeddingService.GenerateEmbeddingsAsync()` |
| **Vector Store（向量库）** | 持久化存储 (text, vector) 对，支持近邻检索 | SQLite（内存余弦，适合本地开发）；CosmosDB DiskANN（ANN索引，生产用） | `SqliteVectorStore` · `CosmosDbVectorStore` |
| **ANN / 近似最近邻** | 用索引结构（HNSW / DiskANN）代替暴力扫描，牺牲少量精度换速度 | CosmosDB 容器配置 DiskANN 向量索引（余弦距离）；SQLite 全量扫描（精确，适合小规模） | `CosmosDbVectorStore.SearchAsync()` SQL 中 `VectorDistance()` |
| **Cosine Similarity（余弦相似度）** | 两向量夹角的余弦值，[-1,1]，越接近 1 语义越近 | `dot / (normA × normB)`，所有场景统一使用此公式 | `VectorMath.CosineSimilarity()` |
| **Semantic Search（语义检索）** | 通过向量相似度而非关键词匹配来检索相关内容 | 对 queryEmbedding 执行 SearchAsync，按相似度降序，过滤 minSimilarity | `IVectorStore.SearchAsync()` |
| **Keyword Search（关键词检索）** | 传统 BM25 / LIKE 匹配，对精确词汇命中率高 | SQLite: LIKE 内存过滤；CosmosDB: CONTAINS 全文匹配 | `IVectorStore.SearchByKeywordsAsync()` |
| **Hybrid Retrieval（混合检索）** | 向量检索 + 关键词检索双通道，融合结果 | `HybridRetriever`：顺序执行两通道，再 RRF 或加权融合 | `HybridRetriever.RetrieveAsync()` |
| **RRF（Reciprocal Rank Fusion）** | 多路检索结果融合算法，score = Σ 1/(k+rank)，k=60 | 两通道按排名计算 RRF 分，同一文档累加 | `HybridRetriever.FuseRrf()` |
| **Reranking（重排序）** | 对检索候选用更精确的评分重新排序 | 轻量重排：70%向量相似度 + 30%关键词覆盖率（无额外 LLM）；Phase 5 可替换 cross-encoder | `QueryService.Rerank()` |
| **Context Window（上下文窗口）** | LLM 一次能处理的最大 token 数量，需要在此范围内装入最相关的 chunk | Token 预算裁剪（默认 3000 tokens），3字符/token估算，按相似度贪心选取 | `ContextWindowBuilder.Build()` |
| **Prompt Engineering** | 设计系统提示和用户消息，引导 LLM 生成期望输出 | System Prompt 支持从 DB 动态加载（`rag-system` 模板），含 `{today}` 占位符 | `QueryService.BuildSystemPromptAsync()` |
| **CoT（Chain-of-Thought）** | 在 Prompt 中注入"逐步推导"指令，提升复杂问题质量 | `ChainOfThoughtStrategy.Enhance()`：注入"1.找信息片段 2.推导 3.结论"步骤 | `ChainOfThoughtStrategy.Enhance()` |
| **RAG（Retrieval-Augmented Generation）** | 检索相关文档片段后注入 LLM Prompt，减少幻觉、引入私有知识 | 完整 RAG 管道：Embed → Search → Rerank → BuildContext → LLM | `QueryService.QueryAsync()` |
| **Hallucination Detection（幻觉检测）** | 验证 LLM 回答是否有文档依据 | 双层：①答案 Embedding 与库相似度 <0.3 → 幻觉；②LLM 自我校验（可选） | `QueryService` + `HallucinationGuardService` |
| **Semantic Cache（语义缓存）** | 对语义相似问题复用历史答案，减少 LLM 调用 | 缓存问题 Embedding，命中阈值 0.95，带 TTL；知识库变更时清除 | `SqliteSemanticCache` / `CosmosDbSemanticCache` |
| **Semantic Dedup（语义去重）** | 摄取时过滤与已有内容高度相似的 chunk | 两层：①SHA-256 精确去重；②Embedding 余弦 ≥0.95 模糊去重 | `DocumentIngestService.IngestAsync()` + `SqliteVectorStore.UpsertBatchAsync()` |
| **Query Expansion（查询扩展）** | 把用户查询扩展为更丰富的形式，提升检索召回率 | 基于 JSON 词库文件，将缩写/自定义词汇替换为规范同义词 | `PersonalVocabularyEnhancer.ExpandQueryAsync()` |
| **Document Versioning（文档版本化）** | 同名文档更新时保留历史版本，查询只返回最新有效版本 | `SupersededAtTicks` 标记取代时间；查询 `Where SupersededAtTicks == 0` | `IVectorStore.MarkDocumentSupersededAsync()` |
| **Multimodal RAG（多模态）** | 处理图片、PDF 等非文本格式，提取文字后进入标准 RAG 管道 | 文件路由：RichMedia → GPT-4o-mini Vision；其他 → Azure Document Intelligence | `VisionModelFileExtractor` · `DocumentIntelligenceFileExtractor` |
| **Knowledge Scope（知识作用域）** | 多用户场景下隔离不同用户/组织的知识库 | `KnowledgeScope(Domain, OwnerId)` 作为过滤条件；支持用户私有 + 共享组 | `IVectorStore.SearchAsync(scope: ...)` · `KnowledgeGovernanceService` |
| **Feedback-based Boost（反馈增强）** | 根据用户历史接受/拒绝行为动态调整 chunk 排名权重 | Accept+0.2 / Reject-0.15，clamp [0.3, 2.0]，乘以 rerank score | `FeedbackBoostService.ApplyBoostAsync()` |
| **Structured Output（结构化输出）** | 强制 LLM 按 JSON schema 输出，便于程序解析 | Prompt 要求返回 `{type, summary, evidence, confidence}`，带安全降级解析 | `QueryService.BuildStructuredPrompt()` · `StructuredOutputParser.TryParse()` |
| **IRCoT（Interleaved Retrieval CoT）** | LLM 自主决定何时检索知识库，交替推理和检索，每步结果影响下一步 | Semantic Kernel `ChatCompletionAgent` + `VedaKernelPlugin`，`FunctionChoiceBehavior.Auto()` | `LlmOrchestrationService.RunQueryFlowAsync()` · `VedaKernelPlugin` |
| **MCP（Model Context Protocol）** | 标准化 LLM 工具调用协议，让外部 LLM 调用本系统能力 | `Veda.MCP` 提供 MCP Server：`search_knowledge_base`, `ingest_document`, `list_documents` | `KnowledgeBaseTools` · `IngestTools` |
| **Evaluation（评估）** | 量化 RAG 系统的回答质量，指导优化方向 | 三维评分：Faithfulness（忠实度）/ AnswerRelevancy（相关性）/ ContextRecall（召回率） | `EvaluationRunner` · `FaithfulnessScorer` · `AnswerRelevancyScorer` · `ContextRecallScorer` |
| **Faithfulness（忠实度）** | 回答是否完全来自检索到的上下文，不捏造 | LLM 打分 [0,1]："回答是否完全由 Context 支撑" | `FaithfulnessScorer.ScoreAsync()` |
| **Answer Relevancy（答案相关性）** | 回答是否切回用户问题 | 问题 Embedding 与答案 Embedding 的余弦相似度 | `AnswerRelevancyScorer.ScoreAsync()` |
| **Context Recall（上下文召回率）** | 检索结果是否覆盖了正确答案所需的信息 | 期望答案 Embedding 与检索结果中最高相似度 | `ContextRecallScorer.ScoreAsync()` |
| **LLM Router（模型路由）** | 根据请求复杂度选择合适的 LLM，平衡成本和质量 | Simple → GPT-4o-mini / Ollama qwen3；Advanced → DeepSeek（失败降级） | `LlmRouterService.Resolve()` |
| **SSE（Server-Sent Events）** | 服务器逐 token 推送流式输出，实现"打字机"效果 | `GET /api/querystream`，先推 sources，再逐 token，最后 done | `QueryStreamController` · `QueryService.QueryStreamAsync()` |
| **Data Source Connector** | 自动从外部数据源拉取并摄取文档，MCP Client 模式 | `FileSystemConnector`（本地目录）和 `BlobStorageConnector`（Azure Blob），增量同步（哈希对比） | `FileSystemConnector` · `BlobStorageConnector` · `DataSourceSyncBackgroundService` |

---

## 技术栈速查

| 层面 | 技术 |
|------|------|
| **Web 框架** | ASP.NET Core 9 |
| **LLM 框架** | Microsoft Semantic Kernel |
| **LLM 提供商** | Azure OpenAI（生产）/ Ollama（本地） |
| **Embedding 模型** | text-embedding-3-small（1536维）/ bge-m3（1024维） |
| **Chat 模型** | gpt-4o-mini（Simple）/ DeepSeek-chat（Advanced）/ qwen3:8b（本地） |
| **向量库（生产）** | Azure CosmosDB for NoSQL + DiskANN |
| **向量库（本地）** | SQLite + EF Core（内存余弦相似度） |
| **文件提取** | Azure AI Document Intelligence + GPT-4o-mini Vision |
| **API 接口** | REST + GraphQL（HotChocolate）+ SSE + MCP |
| **评估框架** | 自研（Golden Dataset + LLM 评分器） |
| **部署** | Azure Container Apps |
