> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[05-concept-code-map.cn.md](05-concept-code-map.cn.md)

# 05 — RAG Concept ↔ Code Mapping

> Maps standard RAG terminology to concrete implementation locations in VedaAide.  
> Useful for interview preparation, onboarding, or quickly locating code during technical discussions.

---

## Concept Mapping Table

| RAG Concept | Technical Essence | VedaAide Implementation | Code Location |
|-------------|------------------|------------------------|---------------|
| **Chunking** | Split long documents into short fragments suitable for embedding, controlling token count | Sliding window, dynamically selects TokenSize / OverlapTokens by DocumentType | `TextDocumentProcessor.Process()` · `ChunkingOptions.ForDocumentType()` |
| **Embedding** | Map text to high-dimensional vectors using a language model; semantically similar texts have close vectors | Supports Azure OpenAI `text-embedding-3-small` (1536 dims) and Ollama `bge-m3` (1024 dims), switchable via config | `EmbeddingService.GenerateEmbeddingsAsync()` |
| **Vector Store** | Persist (text, vector) pairs and support nearest-neighbor retrieval | SQLite (in-memory cosine, for local dev); CosmosDB DiskANN (ANN index, for production) | `SqliteVectorStore` · `CosmosDbVectorStore` |
| **ANN / Approximate Nearest Neighbor** | Use index structures (HNSW / DiskANN) instead of brute-force scan, trading small accuracy loss for speed | CosmosDB container configured with DiskANN vector index (cosine distance); SQLite uses exact full scan | `CosmosDbVectorStore.SearchAsync()` SQL with `VectorDistance()` |
| **Cosine Similarity** | Cosine of the angle between two vectors, range [-1,1]; closer to 1 means more semantically similar | `dot / (normA × normB)`, used uniformly across all scenarios | `VectorMath.CosineSimilarity()` |
| **Semantic Search** | Retrieve relevant content by vector similarity rather than keyword matching | Execute SearchAsync on queryEmbedding, sort by similarity descending, filter by minSimilarity | `IVectorStore.SearchAsync()` |
| **Keyword Search** | Traditional BM25 / LIKE matching, high precision for exact terms | SQLite: LIKE in-memory filter; CosmosDB: CONTAINS full-text match | `IVectorStore.SearchByKeywordsAsync()` |
| **Hybrid Retrieval** | Vector + keyword dual-channel retrieval with fused results | `HybridRetriever`: runs both channels sequentially, then RRF or weighted fusion | `HybridRetriever.RetrieveAsync()` |
| **RRF (Reciprocal Rank Fusion)** | Multi-channel result fusion: score = Σ 1/(k+rank), k=60 | Both channels compute RRF scores by rank; same document's scores are accumulated | `HybridRetriever.FuseRrf()` |
| **Reranking** | Re-score retrieval candidates with a more precise scorer | Lightweight rerank: 70% vector similarity + 30% keyword coverage (no extra LLM); Phase 5 can replace with cross-encoder | `QueryService.Rerank()` |
| **Context Window** | Maximum tokens an LLM can process at once; must fit the most relevant chunks within this budget | Token budget trimming (default 3000 tokens), ~3 chars/token estimate, greedy selection by similarity | `ContextWindowBuilder.Build()` |
| **Prompt Engineering** | Design system prompts and user messages to guide LLM toward desired output | System Prompt supports dynamic loading from DB (`rag-system` template) with `{today}` placeholder | `QueryService.BuildSystemPromptAsync()` |
| **CoT (Chain-of-Thought)** | Inject "step-by-step reasoning" instructions into prompt to improve complex question quality | `ChainOfThoughtStrategy.Enhance()`: injects "1.Find fragments 2.Reason 3.Conclude" steps | `ChainOfThoughtStrategy.Enhance()` |
| **RAG (Retrieval-Augmented Generation)** | Retrieve relevant document chunks and inject into LLM prompt to reduce hallucination and introduce private knowledge | Full RAG pipeline: Embed → Search → Rerank → BuildContext → LLM | `QueryService.QueryAsync()` |
| **Hallucination Detection** | Verify whether LLM answers are grounded in documents | Two layers: ① answer embedding similarity to KB < 0.3 → hallucination; ② LLM self-check (optional) | `QueryService` + `HallucinationGuardService` |
| **Semantic Cache** | Reuse historical answers for semantically similar questions, reducing LLM calls | Cache question embeddings, hit threshold 0.95, with TTL; cleared on KB update | `SqliteSemanticCache` / `CosmosDbSemanticCache` |
| **Semantic Dedup** | Filter chunks highly similar to existing content during ingestion | Two layers: ① SHA-256 exact dedup; ② embedding cosine ≥ 0.95 fuzzy dedup | `DocumentIngestService.IngestAsync()` + `SqliteVectorStore.UpsertBatchAsync()` |
| **Query Expansion** | Expand user queries into richer forms to improve retrieval recall | JSON vocabulary file-based — replaces abbreviations/custom terms with canonical synonyms | `PersonalVocabularyEnhancer.ExpandQueryAsync()` |
| **Document Versioning** | Retain historical versions on same-name document update; queries return only the latest valid version | `SupersededAtTicks` marks supersession time; query filters `Where SupersededAtTicks == 0` | `IVectorStore.MarkDocumentSupersededAsync()` |
| **Multimodal RAG** | Process images, PDFs, and other non-text formats; extract text then enter standard RAG pipeline | File routing: RichMedia → GPT-4o-mini Vision; others → Azure Document Intelligence | `VisionModelFileExtractor` · `DocumentIntelligenceFileExtractor` |
| **Knowledge Scope** | Isolate knowledge bases for different users/organizations in multi-tenant scenarios | `KnowledgeScope(Domain, OwnerId)` as filter; supports private user + sharing groups | `IVectorStore.SearchAsync(scope: ...)` · `KnowledgeGovernanceService` |
| **Feedback-based Boost** | Dynamically adjust chunk ranking weights based on user accept/reject history | Accept+0.2 / Reject-0.15, clamp [0.3, 2.0], multiplied by rerank score | `FeedbackBoostService.ApplyBoostAsync()` |
| **Structured Output** | Force LLM to output a JSON schema for programmatic parsing | Prompt requires `{type, summary, evidence, confidence}` with safe fallback parsing | `QueryService.BuildStructuredPrompt()` · `StructuredOutputParser.TryParse()` |
| **IRCoT (Interleaved Retrieval CoT)** | LLM autonomously decides when to retrieve; alternates reasoning and retrieval, each step informing the next | Semantic Kernel `ChatCompletionAgent` + `VedaKernelPlugin`, `FunctionChoiceBehavior.Auto()` | `LlmOrchestrationService.RunQueryFlowAsync()` · `VedaKernelPlugin` |
| **MCP (Model Context Protocol)** | Standardized LLM tool-call protocol enabling external LLMs to call system capabilities | `Veda.MCP` provides MCP Server: `search_knowledge_base`, `ingest_document`, `list_documents` | `KnowledgeBaseTools` · `IngestTools` |
| **Evaluation** | Quantify RAG answer quality to guide optimization | Three-dimensional scoring: Faithfulness / AnswerRelevancy / ContextRecall | `EvaluationRunner` · `FaithfulnessScorer` · `AnswerRelevancyScorer` · `ContextRecallScorer` |
| **Faithfulness** | Whether the answer is entirely derived from retrieved context, without fabrication | LLM scores [0,1]: "Is the answer fully supported by Context?" | `FaithfulnessScorer.ScoreAsync()` |
| **Answer Relevancy** | Whether the answer addresses the user's question | Cosine similarity between question embedding and answer embedding | `AnswerRelevancyScorer.ScoreAsync()` |
| **Context Recall** | Whether retrieval results cover the information needed for the correct answer | Highest similarity between expected-answer embedding and retrieval results | `ContextRecallScorer.ScoreAsync()` |
| **LLM Router** | Select appropriate LLM by request complexity, balancing cost and quality | Simple → GPT-4o-mini / Ollama qwen3; Advanced → DeepSeek (fallback on failure) | `LlmRouterService.Resolve()` |
| **SSE (Server-Sent Events)** | Server streams tokens to client for "typewriter" effect | `GET /api/querystream`: push sources first, then tokens, then `done` | `QueryStreamController` · `QueryService.QueryStreamAsync()` |
| **Data Source Connector** | Automatically pull and ingest documents from external data sources (MCP Client mode) | `FileSystemConnector` (local directory) and `BlobStorageConnector` (Azure Blob), incremental sync via hash comparison | `FileSystemConnector` · `BlobStorageConnector` · `DataSourceSyncBackgroundService` |

---

## Technology Stack Quick Reference

| Layer | Technology |
|-------|-----------|
| **Web Framework** | ASP.NET Core 9 |
| **LLM Framework** | Microsoft Semantic Kernel |
| **LLM Provider** | Azure OpenAI (production) / Ollama (local) |
| **Embedding Model** | text-embedding-3-small (1536 dims) / bge-m3 (1024 dims) |
| **Chat Model** | gpt-4o-mini (Simple) / DeepSeek-chat (Advanced) / qwen3:8b (local) |
| **Vector Store (production)** | Azure CosmosDB for NoSQL + DiskANN |
| **Vector Store (local)** | SQLite + EF Core (in-memory cosine similarity) |
| **File Extraction** | Azure AI Document Intelligence + GPT-4o-mini Vision |
| **API Interface** | REST + GraphQL (HotChocolate) + SSE + MCP |
| **Evaluation Framework** | Custom-built (Golden Dataset + LLM scorers) |
| **Deployment** | Azure Container Apps |
