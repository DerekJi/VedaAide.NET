# 💡 Engineering Insights

这个目录记录项目开发过程中遇到的**非显而易见的设计决策**——那些在面试、技术交流、或自我复盘时值得深入展开的细节。

不是大路货，而是真实踩过、思考过、有观点的东西。

---

## 文件列表

| 文件 | 主题 | 阶段 |
|---|---|---|
| [embedding-local-ollama.cn.md](embedding-local-ollama.cn.md) | 本地 Ollama Embedding：为什么不用云端 API，以及 DIP 在 AI 服务层的应用 | Phase 1 |
| [chunking-strategy.cn.md](chunking-strategy.cn.md) | 动态分块策略：为什么不同文档类型需要不同颗粒度，Overlap 的作用 | Phase 1 |
| [dedup-dual-layer.cn.md](dedup-dual-layer.cn.md) | 双层去重：哈希去重 + 向量相似度去重的互补关系 | Phase 2 |
| [anti-hallucination.cn.md](anti-hallucination.cn.md) | 双层防幻觉：Answer Embedding Check + LLM Self-Check，以及标记 vs. 拦截的产品决策 | Phase 2 |
| [reranking.cn.md](reranking.cn.md) | 轻量 Reranking：2×TopK 宽检索 + 向量/关键词混合重排，时间范围元数据过滤 | Phase 2 |
| [rag-prompt-boundary.cn.md](rag-prompt-boundary.cn.md) | RAG Prompt 推理边界：严格模式 vs. 合理推断，以及 MCP/Agent 场景下的不同要求 | Phase 3 |
| [mcp-dual-role.cn.md](mcp-dual-role.cn.md) | MCP 双重角色：VedaAide 同时作为 MCP Server（已完成）和 MCP Client（待实现）的设计决策 | Phase 4 |
| [agent-patterns.cn.md](agent-patterns.cn.md) | Agent 编排模式：确定性调用链 vs. 真正 LLM 驱动 Agent，EvalAgent 两种时序 | Phase 4 |
| [cot-ircot.cn.md](cot-ircot.cn.md) | CoT 与 IRCoT：Prompt 推理技巧和检索增强推理的关系与演进路径 | Phase 4 |