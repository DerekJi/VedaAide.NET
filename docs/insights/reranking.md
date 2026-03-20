# 轻量 Reranking：为什么检索 2×TopK 再重排

**项目**：VedaAide.NET  
**涉及文件**：`src/Veda.Services/QueryService.cs` → `Rerank()`、`src/Veda.Core/RagDefaults.cs`  
**阶段**：Phase 2

---

## 背景：向量检索的盲点

RAG 最基础的实现是：生成问题的 Embedding → 向量库检索 Top5 → 送给 LLM。

这有一个根本性问题：**向量相似度只衡量语义接近，不衡量字面相关性**。

例如："水费账单多少钱" 这个问题，向量上最接近的可能是"电费账单"、"燃气费账单"——都是账单语义，但不是最相关的内容。真正最有用的 chunk 可能因为只有 0.01 的相似度差距而排在第 6 位，被 Top5 截掉。

---

## 解决方案：先宽检索，再重排

```
检索 TopK × 2 = 10 个候选（开口更大，不容易漏掉关键内容）
    ↓
轻量重排：70% 向量相似度 + 30% 关键词覆盖率
    ↓
取重排后的 Top5 作为最终 context
```

**关键词覆盖率**的计算逻辑：

```csharp
// QueryService.cs
var questionWords = question.Split(' ').Select(w => w.ToLowerInvariant()).ToHashSet();
var overlapScore = (float)contentWords.Count(w => questionWords.Contains(w)) / questionWords.Count;
var combined = 0.7f * vectorSimilarity + 0.3f * overlapScore;
```

问题里出现的词，如果也在 chunk 里出现，得分加成。这可以把字面高度匹配但向量稍低的 chunk 拉上来。

---

## 为什么是 70/30 的权重

- 向量相似度捕捉**语义**（同义词、上下文关联）
- 关键词覆盖率捕捉**字面精确性**（数字、专有名词、账单类目名）

纯向量：容易被"相关话题"的内容干扰  
纯关键词（BM25）：无法识别同义词，中文分词效果差  
**混合**：取两者的长处

70/30 是业界 hybrid search 的经验起点，可根据具体数据集调整。

---

## 局限性与升级路径

当前的关键词覆盖率基于**空格分词**，对中文几乎无效（中文词之间没有空格）。

```csharp
question.Split(' ', StringSplitOptions.RemoveEmptyEntries)  // 中文会变成整句一个 token
```

这是已知局限，Phase 4 的升级路径：
1. **短期**：引入 jieba.NET 或类似中文分词库
2. **中期**：用 BM25 替代简单关键词覆盖
3. **长期**：用 cross-encoder 模型做精排（如 `bge-reranker`），完全替换当前轻量策略

接口已预留（目前是 `QueryService` 内的私有方法 `Rerank()`），Phase 4 可提取为 `IRerankService` 注入。

---

## 时间范围元数据过滤

与 Reranking 配合使用的另一个检索优化：`RagQueryRequest` 支持 `DateFrom`/`DateTo` 参数，在向量检索的 `WHERE` 子句中过滤 `CreatedAtTicks`。

```sql
-- SqliteVectorStore 内部查询
WHERE created_at_ticks >= @dateFrom AND created_at_ticks <= @dateTo
```

这对时间敏感的查询（"上个月的账单"、"最近一次记录"）非常关键——不加时间过滤，跨期的相似内容会干扰结果。

---

## 面试延伸点

- RAG 检索的两阶段范式：recall（宽检索，保证不漏）→ precision（重排，提升准确）
- Hybrid Search（向量 + 关键词混合）的工业实践：Elasticsearch、Azure AI Search 都内置了这个模式
- Cross-encoder vs. Bi-encoder：为什么 cross-encoder 精排效果更好但不能用于初始检索（计算量 O(n²) 无法扩展）
- 元数据过滤 + 向量检索的结合：ANNS（近似最近邻搜索）为什么需要 pre-filter 而不是 post-filter
