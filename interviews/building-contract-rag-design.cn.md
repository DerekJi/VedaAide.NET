# 建筑行业合同/图纸问答系统 - RAG架构设计

> **面试题**: 我们需要为建筑行业的客户建立一个合同/图纸问答系统。请口述或在白板上设计一个基于 .NET 的 RAG 流程。

## 📋 .NET RAG 系统架构设计（建筑行业）

### 一、系统分层架构

```
┌─────────────────────────────────────────────────────────┐
│  前端层：Angular 19 + 实时流式UI（Server-Sent Events） │
├─────────────────────────────────────────────────────────┤
│  API层：ASP.NET Core + GraphQL + REST + MCP Server      │
├─────────────────────────────────────────────────────────┤
│  AI编排层：Semantic Kernel 1.73 + Agent Orchestration  │
├─────────────────────────────────────────────────────────┤
│  RAG引擎层：摄取 + 检索 + 生成 + 防幻觉                  │
├─────────────────────────────────────────────────────────┤
│  数据层：EF Core + 向量存储（SQLite/Azure Cosmos DB）   │
└─────────────────────────────────────────────────────────┘
```

---

### 二、核心RAG流程

RAG系统分为两个主要部分：

#### **Part A: 离线流程（数据准备） - Ingestion Pipeline**

```
合同PDF/图纸 → 1.文本提取 → 2.智能分块 → 3.语义增强 → 4.Embedding生成 → 5.语义去重 → 6.存储
```

**详细步骤：**

**步骤1：多策略文本提取**（建筑文档特点：扫描图纸多、表格复杂）
- 纯文字PDF：`PdfTextLayerExtractor`（PdfPig底层，快速直通）
- 扫描图纸：`Azure Document Intelligence`（OCR + 表格识别）
- 降级方案：`Vision Model`（配额超限时fallback）

**步骤2：智能分块策略**（按文档类型差异化）
```csharp
DocumentType.Contract → ChunkingOptions(512 tokens, 64 overlap)
DocumentType.Blueprint → ChunkingOptions(1024 tokens, 128 overlap) // 图纸上下文更长
DocumentType.Invoice → ChunkingOptions(256 tokens, 32 overlap)
```

**步骤3：语义增强**（建筑领域专业术语处理）
- **作用**：为每个chunk.Metadata附加别名标签，**不修改chunk.Content**
- **时机**：在Embedding生成之前执行
- **用途**：主要用于查询阶段的Query Expansion（查询扩展）
```csharp
// 示例：个人词库别名扩展
foreach (var chunk in chunks) {
    var aliasTags = await semanticEnhancer.GetAliasTagsAsync(chunk.Content, ct);
    if (aliasTags.Count > 0)
        chunk.Metadata["aliasTags"] = string.Join(",", aliasTags);
}

// 建筑领域示例：
"CAD" → aliasTags: ["Computer-Aided Design", "计算机辅助设计"]
"BIM" → aliasTags: ["Building Information Modeling", "建筑信息模型"]
```

**步骤4：Embedding生成**
- 本地：`bge-m3`（Ollama托管，私有化部署）
- 云端：`text-embedding-3-large`（Azure OpenAI）
```csharp
var embeddings = await embeddingService.GenerateEmbeddingsAsync(
    chunks.Select(c => c.Content).ToList(), ct);
for (int i = 0; i < chunks.Count; i++)
    chunks[i].Embedding = embeddings[i];
```

**步骤5：双层去重机制**
- **第一层（内容级）**：SHA-256哈希去重，完全相同的文件跳过（在数据源同步阶段）
- **第二层（语义级）**：向量相似度去重（阈值0.95），发生在Embedding生成**之后**
  ```csharp
  // 逐个chunk查询向量库，过滤语义近似重复
  var deduped = new List<DocumentChunk>();
  foreach (var chunk in chunks) {
      var similar = await vectorStore.SearchAsync(
          chunk.Embedding, topK: 1, minSimilarity: 0.95);
      if (similar.Count == 0) deduped.Add(chunk); // 无近似重复，保留
  }
  ```

**步骤6：版本化管理与存储**
- 合同修订时自动标记旧版本（`SupersededAtTicks`）
- 保留所有历史版本，支持追溯
- 存储到向量数据库（SQLite开发 / Cosmos DB生产）

---

#### **Part B: 在线流程（查询响应） - Query Pipeline**

##### **阶段1：混合检索（Hybrid Retrieval）**

```
用户问题 → 查询扩展(Query Expansion) → Query Embedding → 向量检索 ┐
                                                         │→ RRF融合 → 重排序 → TopK结果
                              Query Keywords → 关键词检索 ┘
```

**关键实现点：**

1. **查询扩展（Query Expansion）**
   - **作用**：利用步骤3中存储的别名标签，自动扩展用户查询
   - **实现**：从个人词库中查找同义词/别名，补全到查询中
   ```csharp
   // 用户问题："BIM模型在哪里？"
   var expandedQuery = await semanticEnhancer.ExpandQueryAsync(question, ct);
   // 扩展后："BIM模型 OR 建筑信息模型 OR Building Information Modeling"
   ```

2. **双通道并行检索**
   - **向量通道**：语义相似度检索（捕获隐含意图）
   - **关键词通道**：BM25算法（精确匹配合同编号、条款号）

3. **RRF融合策略**（Reciprocal Rank Fusion）
   - 先取TopK×4候选集（如10个文档块）。
   - 使用排名倒数公式融合多通道检索结果：
     $$
     \text{RRF Score} = \frac{1}{k + \text{Rank}}
     $$
   - RRF_K = 60; // 标准值，抑制头部集中效应。
   - 输出候选文档块列表，供后续 Reranking 使用。

4. **轻量重排（Reranking）**
   - 对 RRF 输出的候选文档块进行精细排序。
   - 结合向量相似度和关键词覆盖率，计算综合得分：
     $$
     \text{Combined Score} = 0.7 \times \text{Similarity} + 0.3 \times \text{Overlap Score}
     $$
   - 输出最终的 TopK 文档块，供上下文构建和生成使用。

5. **反馈提升（Feedback Boost）**
   - 用户点赞的chunk在下次检索时提升排名
   - 持续优化个性化结果

---

##### **阶段2：LLM生成与防幻觉**

```
检索结果 → 上下文窗口构建 → LLM生成答案 → 双层防幻觉检测 → 返回答案
```

**关键实现点：**

1. **上下文窗口构建**
   ```csharp
   SystemPrompt = $"今天日期：{DateTime.Now:yyyy-MM-dd}\n"
                + "你是建筑合同专家，依据以下Context回答问题...\n"
                + "必须引用具体条款号和页码...";
   
   UserPrompt = $"Context:\n{合并TopK文档块}\n\nQuestion: {用户问题}";
   ```

2. **LLM路由**
   - 本地部署：`qwen3:8b`（Ollama，离线可用）
   - 云端高质量：`gpt-4o`/`DeepSeek-R1`（复杂推理场景）

3. **双层防幻觉机制**
   - **第一层（入口防护）**：最低相似度阈值（MinSimilarity=0.6）
     ```csharp
     if (topChunks.Count == 0 || topChunks[0].Similarity < 0.6)
         return "I don't have enough information in the provided documents.";
     ```
   - **第二层（出口校验）**：LLM自查机制
     ```csharp
     HallucinationGuardService:
       调用LLM逐句审核答案是否有文档依据
       返回 bool（true=通过，false=幻觉风险）
     ```

4. **语义缓存**
   - 相同问题的embedding缓存答案（避免重复LLM调用）
   - 知识库更新后自动清空缓存

---

##### **阶段3：高级编排（Agent + IRCoT）**

```
复杂问题 → Agent判断 → 迭代检索 → 思维链推理 → 多轮问答 → 最终答案
```

**关键实现点：**

1. **IRCoT策略**（Interleaved Retrieval + Chain-of-Thought）
   - 问题分解 → 检索1 → 部分推理 → 检索2 → 完整推理
   - 适用场景：跨合同对比、多层级条款关联分析

2. **Agent类型**（Semantic Kernel）
   - **QueryAgent**：问答主Agent
   - **ContractAnalysisAgent**：合同条款分析专家
   - **BlueprintAgent**：图纸信息提取Agent

---

### 三、建筑行业特定优化

| 优化点 | 实现方案 |
|--------|----------|
| **图纸表格识别** | Azure Document Intelligence Layout API |
| **合同条款定位** | Metadata存储页码、条款号，检索时返回精确位置 |
| **多版本对比** | DocumentDiffService（LLM驱动的Delta分析） |
| **专业术语处理** | PersonalVocabularyEnhancer（建筑词库预置） |
| **合规检查** | Agent编排：检索相关法规 + LLM对比 |

---

### 四、技术栈总结

| 层级 | 技术选型 |
|------|----------|
| 后端框架 | .NET 10 + ASP.NET Core + EF Core 10 |
| AI编排 | Semantic Kernel 1.73 |
| LLM/Embedding | Ollama（本地）/ Azure OpenAI（云端） |
| 向量数据库 | SQLite（开发）/ Cosmos DB（生产） |
| 文档处理 | Azure Document Intelligence + Vision Model |
| API层 | GraphQL（HotChocolate 15）+ SSE流式 |

---

### 五、白板设计图（流程简化版）

```
┌──────────┐     ┌──────────────┐     ┌──────────┐
│ 合同PDF  │────▶│ 文本提取       │────▶│ 分块器   │
│ 图纸DWG  │     │ (Azure DI)    │     │ 512 token│
└──────────┘     └──────────────┘     └──────┬───┘
                                              │
                 ┌──────────────┐     ┌───────▼───┐
                 │ 向量数据库    │◀────│ Embedding │
                 │ (Cosmos DB)  │     │ (bge-m3)  │
                 └──────┬───────┘     └───────────┘
                        │
        ┌───────────────┼───────────────┐
        │ 检索          │               │
        │               │               │
  ┌─────▼─────┐  ┌──────▼──────┐ ┌─────▼──────┐
  │ 向量检索   │  │ 关键词检索   │ │ RRF融合    │
  │ (Semantic)│  │ (BM25)      │ │            │
  └───────────┘  └─────────────┘ └─────┬──────┘
                                        │
                                  ┌─────▼──────┐
                                  │ LLM生成    │
                                  │ (qwen3:8b) │
                                  └─────┬──────┘
                                        │
                                  ┌─────▼──────┐
                                  │ 防幻觉检测  │
                                  │ (Guard)    │
                                  └─────┬──────┘
                                        │
                                  ┌─────▼──────┐
                                  │ 返回答案    │
                                  └────────────┘
```

---

### 六、示例对话流程

**Q：** "第12条款中关于延期交付的违约金计算方式是什么？"

**系统处理：**
1. Query Embedding + 关键词提取（"第12条款"、"延期交付"、"违约金"）
2. 混合检索 → 找到TopK=5个相关chunk（包含条款12的文档块）
3. 上下文窗口：合并5个chunk + metadata（页码、条款号）
4. LLM生成答案（带引用源）
5. 防幻觉检测：LLM自查是否所有陈述均有Context支持

**A：** "根据合同第12条款第3小节（第8页），延期交付的违约金按以下方式计算：
- 延期1-7天：合同总价的0.5%/天
- 延期超过7天：合同总价的1%/天，累计不超过10%
引用来源：《XX项目施工合同》v2.3，第8页，条款12.3"

---

### 七、VedaAide.NET 项目实现对照

本设计方案完全基于 VedaAide.NET 项目的实际实现，关键模块对照：

| 设计模块 | 项目实现文件 |
|---------|------------|
| 文档摄取 | [Veda.Services/DocumentIngestService.cs](../src/Veda.Services/DocumentIngestService.cs) |
| 混合检索 | [Veda.Services/HybridRetriever.cs](../src/Veda.Services/HybridRetriever.cs) |
| 查询服务 | [Veda.Services/QueryService.cs](../src/Veda.Services/QueryService.cs) |
| 防幻觉检测 | [Veda.Services/HallucinationGuardService.cs](../src/Veda.Services/HallucinationGuardService.cs) |
| 智能分块 | [Veda.Services/TextDocumentProcessor.cs](../src/Veda.Services/TextDocumentProcessor.cs) |
| Agent编排 | [Veda.Agents/LlmOrchestrationService.cs](../src/Veda.Agents/LlmOrchestrationService.cs) |

---

### 八、面试要点总结

**强调以下技术亮点：**

1. ✅ **私有化部署**：Ollama + SQLite，无需云端依赖
2. ✅ **双层去重**：内容哈希 + 向量相似度，避免重复存储
3. ✅ **混合检索**：向量+关键词双通道RRF融合，精准度更高
4. ✅ **双层防幻觉**：入口阈值 + 出口LLM自查
5. ✅ **版本化管理**：支持合同修订追溯
6. ✅ **Agent编排**：IRCoT策略应对复杂多步推理
7. ✅ **流式响应**：SSE实时输出，提升用户体验
8. ✅ **语义缓存**：减少重复LLM调用，降低成本

**回答策略：**
- 先讲整体架构（5层）
- 再讲核心流程（5个阶段）
- 针对建筑行业特点补充优化点
- 最后引用VedaAide.NET的实战经验佐证

---

*本文档基于 VedaAide.NET 项目实际代码整理，所有技术方案均已验证可行。*
