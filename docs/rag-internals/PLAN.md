# RAG 内部机制文档计划

> 分支：`docs/rag-architecture-diagrams`  
> 目标：帮助自己和团队快速理解 VedaAide RAG 系统的设计，所有图用 PlantUML（不支持时用 Mermaid）。

---

## 文档清单

| # | 文件 | 状态 | 说明 |
|---|------|------|------|
| 1 | [01-system-architecture.md](01-system-architecture.md) | ✅ | 系统整体架构图：6 个 C# 项目的职责分层 + Azure 基础设施 |
| 2 | [02-ingest-flow.md](02-ingest-flow.md) | ✅ | Ingest 数据流：文档/文件如何进入知识库，含分块、Embedding、去重、版本化 |
| 3 | [03-query-flow.md](03-query-flow.md) | ✅ | Query 数据流：一个问题如何被处理，含语义增强、混合检索、Rerank、CoT、防幻觉 |
| 4 | [04-storage-retrieval.md](04-storage-retrieval.md) | ✅ | 存储层与检索模块细图：SQLite vs CosmosDB 双实现、向量检索、关键词检索、RRF 融合 |
| 5 | [05-concept-code-map.md](05-concept-code-map.md) | ✅ | RAG 概念↔代码对照表：面试常谈概念与具体类/方法的映射 |
| 6 | [06-module-dependencies.md](06-module-dependencies.md) | ✅ | 模块依赖拓扑图：6 个项目的依赖方向 + DIP/ISP 边界说明 |
| 7 | [07-data-model-er.md](07-data-model-er.md) | ✅ | 数据模型 ER 图：所有 Entity 的字段与关系 |
| 8 | [08-azure-deployment.md](08-azure-deployment.md) | ✅ | Azure 部署架构图：Container Apps / CosmosDB / OpenAI / Document Intelligence |
| 9 | [09-adr.md](09-adr.md) | ✅ | ADR 架构决策记录：RRF vs WeightedSum、SQLite/CosmosDB 双实现、IRCoT 等关键决策 |

---

## 图形约定

- **PlantUML**（`@startuml`）用于：流程图、序列图、组件图、类图、ER 图
- **Mermaid**（` ```mermaid `）备选用于：思维导图、Git 图、PlantUML 不支持的场景
- 所有图嵌入在 Markdown 文件里，不单独存图片文件

---

## 进度状态

- ⬜ 未开始
- 🔄 进行中
- ✅ 完成

---

## 上下文快照（2026-03-27）

系统已完成四期开发：
- **Phase 1**：SQLite 向量存储、基础 RAG 管道
- **Phase 2**：混合检索（RRF）、防幻觉、语义缓存、CoT、LLM 路由
- **Phase 3**：多模态（Vision / Document Intelligence）、个人词库增强、数据源同步（FileSystem / BlobStorage）、知识治理（共享组、权限）
- **Phase 4**：MCP Server/Client、IRCoT Agent 编排、CosmosDB 向量存储、GraphQL、评估框架（Faithfulness / Relevancy / Recall）
