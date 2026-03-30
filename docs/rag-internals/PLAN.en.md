> **Viewing diagrams:** In browser, install [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension; in VS Code, install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) plugin.

> 中文版：[PLAN.cn.md](PLAN.cn.md)

# RAG Internals — Documentation Plan

This folder contains detailed technical documentation for VedaAide's RAG (Retrieval-Augmented Generation) system, with architecture diagrams drawn in PlantUML.

---

## Document Index

| # | File | Status | Description |
|---|------|--------|-------------|
| 1 | [01-system-architecture.en.md](01-system-architecture.en.md) · [cn](01-system-architecture.cn.md) | ✅ | Overall system architecture: 6 C# project layers + Azure infrastructure |
| 2 | [02-ingest-flow.en.md](02-ingest-flow.en.md) · [cn](02-ingest-flow.cn.md) | ✅ | Ingest pipeline: how documents/files enter the knowledge base (chunking, embedding, dedup, versioning) |
| 3 | [03-query-flow.en.md](03-query-flow.en.md) · [cn](03-query-flow.cn.md) | ✅ | Query pipeline: how a question is processed (semantic expansion, hybrid retrieval, rerank, CoT, hallucination guard) |
| 4 | [04-storage-retrieval.en.md](04-storage-retrieval.en.md) · [cn](04-storage-retrieval.cn.md) | ✅ | Storage layer & retrieval internals: SQLite vs CosmosDB, vector search, keyword search, RRF fusion |
| 5 | [05-concept-code-map.en.md](05-concept-code-map.en.md) · [cn](05-concept-code-map.cn.md) | ✅ | RAG concept ↔ code mapping table: 30 standard RAG terms mapped to specific classes/methods |
| 6 | [06-module-dependencies.en.md](06-module-dependencies.en.md) · [cn](06-module-dependencies.cn.md) | ✅ | Module dependency topology: dependency direction of 6 projects + DIP/ISP boundary explanation |
| 7 | [07-data-model-er.en.md](07-data-model-er.en.md) · [cn](07-data-model-er.cn.md) | ✅ | Data model ER diagram: all entity fields and relationships |
| 8 | [08-azure-deployment.en.md](08-azure-deployment.en.md) · [cn](08-azure-deployment.cn.md) | ✅ | Azure deployment architecture: Container Apps / CosmosDB / OpenAI / Document Intelligence |
| 9 | [09-adr.en.md](09-adr.en.md) · [cn](09-adr.cn.md) | ✅ | Architecture Decision Records: RRF vs WeightedSum, SQLite/CosmosDB dual-impl, IRCoT, and more |

---

## Diagram Conventions

- All architecture diagrams are drawn in **PlantUML** syntax inside fenced code blocks.
- To render diagrams inline:
  - **Browser**: Install the [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) extension (Chrome/Edge).
  - **VS Code**: Install [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown).
- Diagram types used: component, sequence, activity, class, entity-relationship.

---

## Coverage

```
Phase 1: SQLite vector store, basic RAG pipeline, chunking, embedding, dedup
Phase 2: Hybrid retrieval (RRF), reranking, semantic cache, hallucination guard, feedback boost
Phase 3: Azure CosmosDB DiskANN, Document Intelligence, data source connectors
Phase 4: IRCoT agent (Semantic Kernel), MCP Server/Client, evaluation framework
```
