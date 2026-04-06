# Construction Contract/Blueprint Q&A System - RAG Architecture Design

> **Interview Question**: We need to build a contract/blueprint Q&A system for the construction industry. Please verbally describe or design a .NET-based RAG pipeline on a whiteboard.

## 📋 .NET RAG System Architecture (Construction Industry)

### 1. Layered System Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Frontend: Angular 19 + Real-time Streaming UI (SSE)   │
├─────────────────────────────────────────────────────────┤
│  API Layer: ASP.NET Core + GraphQL + REST + MCP Server │
├─────────────────────────────────────────────────────────┤
│  AI Orchestration: Semantic Kernel 1.73 + Agents       │
├─────────────────────────────────────────────────────────┤
│  RAG Engine: Ingestion + Retrieval + Generation + Guard│
├─────────────────────────────────────────────────────────┤
│  Data Layer: EF Core + Vector Store (SQLite/Cosmos DB) │
└─────────────────────────────────────────────────────────┘
```

---

### 2. Core RAG Pipeline (5 Phases)

#### **Phase 1: Document Ingestion**

```
Contract PDF/Blueprint → Text Extraction → Smart Chunking → Vectorization → Deduplication → Storage
```

**Key Implementation Points:**

1. **Multi-Strategy Text Extraction** (construction documents: scanned blueprints, complex tables)
   - Pure text PDF: `PdfTextLayerExtractor` (PdfPig-based, fast direct extraction)
   - Scanned blueprints: `Azure Document Intelligence` (OCR + table recognition)
   - Fallback: `Vision Model` (when quota exceeded)

2. **Smart Chunking Strategy** (differentiated by document type)
   ```csharp
   DocumentType.Contract → ChunkingOptions(512 tokens, 64 overlap)
   DocumentType.Blueprint → ChunkingOptions(1024 tokens, 128 overlap) // longer context for blueprints
   DocumentType.Invoice → ChunkingOptions(256 tokens, 32 overlap)
   ```

3. **Two-Layer Deduplication**
   - **Layer 1 (Content-level)**: SHA-256 hash deduplication, skip identical files (during data source sync)
   - **Layer 2 (Semantic-level)**: Vector similarity deduplication (threshold 0.95), happens **after** embedding generation
     ```csharp
     // After generating embeddings, query vector store for each chunk
     foreach (var chunk in chunks) {
         var similar = await vectorStore.SearchAsync(
             chunk.Embedding, topK: 1, minSimilarity: 0.95);
         if (similar.Count == 0) deduped.Add(chunk); // no similar duplicate, keep it
     }
     ```

4. **Version Control**
   - Retain all historical versions for contract revision traceability

---

#### **Phase 2: Vectorization & Semantic Enhancement**

```
Text Chunks → Embedding Generation → Semantic Enhancement (Alias Tags) → Vector Database
```

**Key Implementation Points:**

1. **Embedding Models**
   - Local: `bge-m3` (hosted by Ollama, private deployment)
   - Cloud: `text-embedding-3-large` (Azure OpenAI)

2. **Semantic Enhancement** (construction domain terminology)
   ```csharp
   // Example: Personal vocabulary alias expansion
   "CAD" → ["Computer-Aided Design", "计算机辅助设计"]
   "BIM" → ["Building Information Modeling", "建筑信息模型"]
   
   // During ingestion: attach aliases to chunk.Metadata["aliasTags"]
   // During retrieval: auto-expand query terms ("BIM model" → "BIM model OR Building Information Modeling")
   ```

3. **Vector Storage**
   - Development: SQLite (EF Core in-memory vectors)
   - Production: Azure Cosmos DB for NoSQL (native vector indexing)

---

#### **Phase 3: Hybrid Retrieval**

```
User Question → Query Embedding → Vector Retrieval ┐
                                  │→ RRF Fusion → Reranking → TopK Results
         Query Keywords → Keyword Retrieval ┘
```

**Key Implementation Points:**

1. **Dual-Channel Parallel Retrieval**
   - **Vector Channel**: Semantic similarity retrieval (captures implicit intent)
   - **Keyword Channel**: BM25 algorithm (exact matching for contract numbers, clause IDs)

2. **RRF Fusion Strategy** (Reciprocal Rank Fusion)
   ```csharp
   // Avoid single-channel bias, formula: score = 1/(k + rank)
   RRF_K = 60; // standard value, suppresses head concentration effect
   FinalScore = VectorScore * 0.7 + KeywordScore * 0.3
   ```

3. **Lightweight Reranking**
   - First retrieve TopK×4 candidates (e.g., 20 document chunks)
   - Rerank by "70% vector similarity + 30% keyword coverage"
   - Take final TopK=5 as context

4. **Feedback Boost**
   - User-upvoted chunks rank higher in subsequent retrievals
   - Continuously optimize personalized results

---

#### **Phase 4: LLM Generation & Hallucination Prevention**

```
Retrieval Results → Context Window Construction → LLM Answer Generation → Two-Layer Hallucination Detection → Return Answer
```

**Key Implementation Points:**

1. **Context Window Construction**
   ```csharp
   SystemPrompt = $"Today's date: {DateTime.Now:yyyy-MM-dd}\n"
                + "You are a construction contract expert. Answer questions based on the Context below...\n"
                + "Must cite specific clause numbers and page references...";
   
   UserPrompt = $"Context:\n{merged TopK chunks}\n\nQuestion: {user question}";
   ```

2. **LLM Routing**
   - Local deployment: `qwen3:8b` (Ollama, offline-capable)
   - Cloud high-quality: `gpt-4o`/`DeepSeek-R1` (complex reasoning scenarios)

3. **Two-Layer Hallucination Prevention**
   - **Layer 1 (Entry Guard)**: Minimum similarity threshold (MinSimilarity=0.6)
     ```csharp
     if (topChunks.Count == 0 || topChunks[0].Similarity < 0.6)
         return "I don't have enough information in the provided documents.";
     ```
   - **Layer 2 (Exit Verification)**: LLM self-check mechanism
     ```csharp
     HallucinationGuardService:
       Call LLM to verify each statement against source documents
       Return bool (true=grounded, false=hallucination risk)
     ```

4. **Semantic Cache**
   - Cache embedding responses for identical questions (avoid redundant LLM calls)
   - Auto-invalidate cache when knowledge base updates

---

#### **Phase 5: Advanced Orchestration (Agent + IRCoT)**

```
Complex Question → Agent Decision → Iterative Retrieval → Chain-of-Thought → Multi-turn Q&A → Final Answer
```

**Key Implementation Points:**

1. **IRCoT Strategy** (Interleaved Retrieval + Chain-of-Thought)
   - Question decomposition → Retrieval 1 → Partial reasoning → Retrieval 2 → Complete reasoning
   - Use cases: Cross-contract comparison, multi-level clause correlation analysis

2. **Agent Types** (Semantic Kernel)
   - **QueryAgent**: Main Q&A agent
   - **ContractAnalysisAgent**: Contract clause analysis expert
   - **BlueprintAgent**: Blueprint information extraction agent

---

### 3. Construction-Specific Optimizations

| Optimization | Implementation |
|--------------|----------------|
| **Blueprint Table Recognition** | Azure Document Intelligence Layout API |
| **Contract Clause Localization** | Metadata stores page numbers, clause IDs for precise location retrieval |
| **Multi-Version Comparison** | DocumentDiffService (LLM-driven Delta analysis) |
| **Domain Terminology** | PersonalVocabularyEnhancer (pre-loaded construction vocabulary) |
| **Compliance Checking** | Agent orchestration: retrieve regulations + LLM comparison |

---

### 4. Technology Stack Summary

| Layer | Tech Stack |
|-------|-----------|
| Backend Framework | .NET 10 + ASP.NET Core + EF Core 10 |
| AI Orchestration | Semantic Kernel 1.73 |
| LLM/Embedding | Ollama (local) / Azure OpenAI (cloud) |
| Vector Database | SQLite (dev) / Cosmos DB (production) |
| Document Processing | Azure Document Intelligence + Vision Model |
| API Layer | GraphQL (HotChocolate 15) + SSE Streaming |

---

### 5. Whiteboard Diagram (Simplified Flow)

```
┌──────────┐     ┌──────────────┐     ┌──────────┐
│ Contract │────▶│ Text Extract │────▶│ Chunker  │
│ PDF/DWG  │     │ (Azure DI)   │     │ 512 token│
└──────────┘     └──────────────┘     └──────┬───┘
                                              │
                 ┌──────────────┐     ┌───────▼───┐
                 │ Vector DB    │◀────│ Embedding │
                 │ (Cosmos DB)  │     │ (bge-m3)  │
                 └──────┬───────┘     └───────────┘
                        │
        ┌───────────────┼───────────────┐
        │ Retrieval     │               │
        │               │               │
  ┌─────▼─────┐  ┌──────▼──────┐ ┌─────▼──────┐
  │ Vector    │  │ Keyword     │ │ RRF Fusion │
  │ (Semantic)│  │ (BM25)      │ │            │
  └───────────┘  └─────────────┘ └─────┬──────┘
                                        │
                                  ┌─────▼──────┐
                                  │ LLM Gen    │
                                  │ (qwen3:8b) │
                                  └─────┬──────┘
                                        │
                                  ┌─────▼──────┐
                                  │ Hallucinate│
                                  │ Guard      │
                                  └─────┬──────┘
                                        │
                                  ┌─────▼──────┐
                                  │ Return     │
                                  └────────────┘
```

---

### 6. Sample Conversation Flow

**Q:** "What is the penalty calculation method for delayed delivery in Clause 12?"

**System Processing:**
1. Query Embedding + keyword extraction ("Clause 12", "delayed delivery", "penalty")
2. Hybrid retrieval → find TopK=5 relevant chunks (containing Clause 12 document blocks)
3. Context window: merge 5 chunks + metadata (page numbers, clause IDs)
4. LLM generates answer (with source citations)
5. Hallucination detection: LLM self-check if all statements are supported by Context

**A:** "According to Contract Clause 12, Section 3 (Page 8), the delayed delivery penalty is calculated as follows:
- Delay 1-7 days: 0.5% of total contract value per day
- Delay over 7 days: 1% of total contract value per day, capped at 10% total
Source: "XX Project Construction Contract" v2.3, Page 8, Clause 12.3"

---

### 7. VedaAide.NET Project Implementation Reference

This design is fully based on the actual implementation of the VedaAide.NET project. Key module mapping:

| Design Module | Project Implementation File |
|---------------|----------------------------|
| Document Ingestion | [Veda.Services/DocumentIngestService.cs](../src/Veda.Services/DocumentIngestService.cs) |
| Hybrid Retrieval | [Veda.Services/HybridRetriever.cs](../src/Veda.Services/HybridRetriever.cs) |
| Query Service | [Veda.Services/QueryService.cs](../src/Veda.Services/QueryService.cs) |
| Hallucination Guard | [Veda.Services/HallucinationGuardService.cs](../src/Veda.Services/HallucinationGuardService.cs) |
| Smart Chunking | [Veda.Services/TextDocumentProcessor.cs](../src/Veda.Services/TextDocumentProcessor.cs) |
| Agent Orchestration | [Veda.Agents/LlmOrchestrationService.cs](../src/Veda.Agents/LlmOrchestrationService.cs) |

---

### 8. Interview Talking Points Summary

**Highlight these technical strengths:**

1. ✅ **Private Deployment**: Ollama + SQLite, no cloud dependency required
2. ✅ **Two-Layer Deduplication**: Content hash + vector similarity, avoid redundant storage
3. ✅ **Hybrid Retrieval**: Vector + keyword dual-channel RRF fusion, higher precision
4. ✅ **Two-Layer Hallucination Guard**: Entry threshold + exit LLM self-check
5. ✅ **Version Control**: Support contract revision traceability
6. ✅ **Agent Orchestration**: IRCoT strategy for complex multi-step reasoning
7. ✅ **Streaming Response**: SSE real-time output for better UX
8. ✅ **Semantic Caching**: Reduce redundant LLM calls, lower costs

**Response Strategy:**
- Start with overall architecture (5 layers)
- Explain core pipeline (5 phases)
- Supplement construction-specific optimizations
- Reference VedaAide.NET production experience as evidence

---

*This document is based on the actual VedaAide.NET project codebase. All technical solutions have been validated in production.*
