> **查看图表说明：** 浏览器安装 [Markdown Diagrams](https://chromewebstore.google.com/detail/markdown-diagrams/mnfehgbmkaijmakeobbflcbldbbldmjh) 扩展；VS Code 安装 [Markdown PlantUML Preview](https://marketplace.visualstudio.com/items?itemName=well-30.plantuml-markdown) 插件。

> English version: [09-adr.en.md](09-adr.en.md)

# 09 — 架构决策记录（ADR）

> 记录 VedaAide 系统中关键技术决策的背景、备选方案、最终选择和权衡理由。  
> 格式：背景 → 备选方案 → 决策 → 后果。

---

## ADR-001：向量存储双实现（SQLite / CosmosDB）

**状态**：已采纳  
**时间**：Phase 1（SQLite）→ Phase 4（CosmosDB 扩展）

### 背景
需要持久化存储文档向量并支持相似度检索，同时要兼顾本地开发体验和生产规模需求。

### 备选方案
| 方案 | 优点 | 缺点 |
|------|------|------|
| 只用 Azure AI Search | 功能强，集成好 | 本地开发需联网、有费用 |
| 只用 pgvector (PostgreSQL) | 开源、精确 ANN | 需维护 PostgreSQL 实例 |
| **SQLite（开发）+ CosmosDB（生产）** | 零依赖本地开发；生产 DiskANN 高性能 | 双实现维护成本 |
| 只用内存存储 | 极简 | 重启丢失，不可用于生产 |

### 决策
通过 `IVectorStore` 接口 + `Veda:StorageProvider` 配置实现可插拔双实现。  
SQLite 使用内存全量余弦扫描（精确），适合 <100K chunks；CosmosDB 使用 DiskANN 近似索引，适合百万级数据。

### 后果
- ✅ 本地开发零基础设施依赖，`dotnet run` 即可启动
- ✅ 生产环境无缝迁移，仅改一个配置项
- ✅ 接口设计倒逼团队保持 DIP，依赖抽象而非实现
- ⚠️ SQLite 全量扫描在 >100K chunk 时性能下降
- ⚠️ 两套实现代码需同步维护

---

## ADR-002：混合检索融合策略选 RRF 而非 WeightedSum

**状态**：已采纳（默认），WeightedSum 保留为备选  
**时间**：Phase 2 Sprint 1

### 背景
混合检索（向量 + 关键词）两路结果需要融合排序。向量相似度和关键词评分量纲不同（前者 [-1,1]，后者是词频匹配数），直接相加需要归一化。

### 备选方案
| 方案 | 描述 | 问题 |
|------|------|------|
| **RRF（Reciprocal Rank Fusion）** | `score += 1/(60+rank)`，只看排名不看分数 | 近似最优，但忽略分数差距 |
| WeightedSum | `score = 0.7 × vectorSim + 0.3 × keywordScore` | 需要归一化；两路分数范围不同导致权重失效 |
| Borda Count | 按排名投票 | 实现复杂，效果类似 RRF |

### 决策
默认使用 RRF，k=60（标准值）。`FusionStrategy` 枚举保留 `WeightedSum` 供用户按需切换（`Veda:Rag:FusionStrategy`）。

### 依据
- RRF 是学术界和工业界混合检索的主流选择（论文实证效果优）
- 不需要分数归一化，对两路检索质量不均衡时鲁棒
- k=60 可有效抑制头部过度集中（长尾文档有机会出现）

### 后果
- ✅ 无需调参即有较好效果
- ✅ 代码简洁，无浮点归一化逻辑
- ⚠️ 放弃了分数绝对值信息（极高相似度的结果不会因此获得更大提升）

---

## ADR-003：防幻觉双层校验而非单层

**状态**：已采纳  
**时间**：Phase 2

### 背景
LLM 容易生成听起来合理但与知识库内容不符的答案（幻觉）。需要在不显著增加延迟的前提下检测并标记。

### 备选方案
| 方案 | 精度 | 延迟 | 成本 |
|------|------|------|------|
| 无校验 | - | 0ms | 0 |
| 只用 Embedding 相似度 | 低（误报率高） | ~100ms | 低 |
| 只用 LLM 自我校验 | 高 | +1 LLM 调用 | 高 |
| **Embedding 校验 + 可选 LLM 校验** | 高 | 按需 | 按需 |

### 决策
- **第一层**（必选）：答案 Embedding 与向量库最高相似度 < 0.3 → 幻觉。快速粗筛，无额外 LLM 调用。
- **第二层**（可选，`EnableSelfCheckGuard=false` 默认关闭）：LLM 逐句核查 "Answer 是否完全由 Context 支撑"。精度高但每次多一次调用。

### 后果
- ✅ 生产环境默认只用第一层，延迟影响极小
- ✅ 高质量场景可开启第二层
- ⚠️ 第一层阈值 0.3 较低，高覆盖优先于高精确（宁可放过也不误判正确答案）
- ⚠️ 第二层开启后每次查询成本加倍

---

## ADR-004：语义缓存默认关闭，由配置项启用

**状态**：已采纳  
**时间**：Phase 2 Sprint 3

### 背景
对语义相似的重复问题缓存 LLM 答案可以大幅降低成本和延迟，但当知识库内容频繁变更时，缓存答案可能过期。

### 决策
- 语义缓存默认 **关闭**（`Veda:SemanticCache:Enabled: false`）
- 命中阈值 0.95（极高相似度才复用）
- 知识库摄取成功后同步清除整个缓存（`ClearAsync()`）
- TTL 默认 1 小时

### 权衡
| 关注点 | 设计选择 |
|-------|---------|
| 答案准确性 | 内容变更后清除缓存，优先准确性 |
| 缓存粒度 | 整体清除（简单可靠），而非精准失效 |
| 命中阈值 | 0.95（极严格），避免语义不同的问题误命中 |

### 后果
- ✅ 知识库稳定时缓存效果显著（相同问题 0 LLM 调用）
- ⚠️ 频繁摄取（如每小时批量同步）会导致缓存频繁失效
- ⚠️ 整体清除在大量缓存时有性能开销（DeleteAll SQL）

---

## ADR-005：Chunking 按 DocumentType 差异化而非固定大小

**状态**：已采纳  
**时间**：Phase 1

### 背景
不同类型文档的信息密度差异巨大：发票每行是独立字段（小块更精准），规格说明书需要上下文（大块更完整）。固定分块大小会导致检索质量下降。

### 决策
`ChunkingOptions.ForDocumentType()` 按文档类型返回不同的 `(TokenSize, OverlapTokens)`。

| DocumentType | TokenSize | OverlapTokens | 理由 |
|--------------|-----------|---------------|------|
| BillInvoice  | 256 | 32 | 每个字段独立，小块精准匹配金额/日期 |
| PersonalNote | 256 | 32 | 记录简短，小块粒度合适 |
| Report / Other | 512 | 64 | 通用大小，兼顾上下文和精度 |
| RichMedia | 512 | 64 | Vision 提取的文字通常是段落 |
| Specification | 1024 | 128 | 规格条款需要大窗口保留技术语境 |

### 后果
- ✅ 发票类查询精度显著提升（不会把两张发票的金额混在一个 chunk）
- ✅ 规格类查询保留足够上下文
- ⚠️ 类型判断依赖文件名推断（`DocumentTypeParser.InferFromName`），可能不准确

---

## ADR-006：IRCoT（LLM 决策检索）而非固定单次检索

**状态**：已采纳（Phase 4，作为 Agent 模式）  
**时间**：Phase 4

### 背景
复杂问题可能需要多次检索：第一次检索提供背景，基于背景再细化查询，直到得到足够信息。固定的"一次检索 → 生成"管道无法处理这类需求。

### 备选方案
| 方案 | 描述 |
|------|------|
| 固定单次检索 RAG | 简单，延迟低 |
| Query Decomposition | 预先把复杂问题分解，再分别检索 | 
| **IRCoT（Interleaved Retrieval CoT）** | LLM 自主决定何时检索，交替推理和检索 |
| ReAct Agent | 类似 IRCoT，LLM 生成 Thought + Action + Observation |

### 决策
Phase 4 引入 `LlmOrchestrationService`，使用 Semantic Kernel `ChatCompletionAgent` + `VedaKernelPlugin`（`search_knowledge_base` KernelFunction），`FunctionChoiceBehavior.Auto()` 让 LLM 自主决定调用时机和次数。

原有 `OrchestrationService`（手动链固定单次）保留，两者通过 DI 注入不同实现。

### 后果
- ✅ 复杂多跳问题质量显著提升
- ✅ LLM 可根据中间结果细化搜索词
- ⚠️ 不确定循环次数，延迟和成本不固定
- ⚠️ 调试困难（需 AgentTrace 记录每步行为）

---

## ADR-007：MCP Server 而非专有 API 集成

**状态**：已采纳  
**时间**：Phase 4

### 背景
随着外部 LLM（Claude、GPT-4 等）能力增强，需要让它们直接访问 VedaAide 知识库。每个 LLM 的工具调用格式不同，专有集成需要为每个 LLM 写适配器。

### 决策
采用 **Model Context Protocol (MCP)** 标准，`Veda.MCP` 项目暴露标准化 MCP Server（HTTP/SSE），提供三个工具：
- `search_knowledge_base`
- `ingest_document`  
- `list_documents`

### 后果
- ✅ 任何支持 MCP 的 LLM/工具（Claude Desktop、Cursor 等）可直接接入
- ✅ VedaAide 同时是 MCP Server（对外暴露能力）和 MCP Client（`DataSourceConnector` 从外部拉数据）
- ⚠️ MCP 协议仍在快速演进，需跟随标准更新
