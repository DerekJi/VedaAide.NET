# 💡 Engineering Insights

This directory records **non-obvious design decisions** made during development — the kind of details worth explaining in depth during interviews, technical discussions, or retrospectives.

Not textbook content, but real decisions that were measured, questioned, and made with conviction.

> 中文版见 [README.cn.md](README.cn.md)

---

## File Index

| File | Topic | Phase |
|---|---|---|
| [embedding-local-ollama.en.md](embedding-local-ollama.en.md) | Local Ollama Embedding: why not cloud APIs, and DIP applied to the AI service layer | Phase 1 |
| [chunking-strategy.en.md](chunking-strategy.en.md) | Dynamic chunking strategy: why different document types need different granularities, and the role of overlap | Phase 1 |
| [dedup-dual-layer.en.md](dedup-dual-layer.en.md) | Dual-layer deduplication: how hash dedup and vector similarity dedup complement each other | Phase 2 |
| [anti-hallucination.en.md](anti-hallucination.en.md) | Dual-layer hallucination detection: Answer Embedding Check + LLM Self-Check, and the flag-vs-block product decision | Phase 2 |
| [reranking.en.md](reranking.en.md) | Lightweight Reranking: 2×TopK wide retrieval + vector/keyword hybrid re-scoring, date-range metadata filtering | Phase 2 |
| [rag-prompt-boundary.en.md](rag-prompt-boundary.en.md) | RAG prompt reasoning boundary: strict mode vs. reasonable inference, and different requirements in MCP/Agent scenarios | Phase 3 |
| [mcp-dual-role.en.md](mcp-dual-role.en.md) | MCP dual role: VedaAide as both MCP Server (done) and MCP Client (done), design decisions | Phase 4 |
| [agent-patterns.en.md](agent-patterns.en.md) | Agent orchestration patterns: deterministic call chain vs. truly LLM-driven agents, two EvalAgent timings | Phase 4 |
| [cot-ircot.en.md](cot-ircot.en.md) | CoT and IRCoT: prompt reasoning techniques and their relationship to retrieval-augmented reasoning | Phase 4 |
