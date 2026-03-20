# 双层防幻觉体系

**项目**：VedaAide.NET  
**涉及文件**：`src/Veda.Services/QueryService.cs`、`src/Veda.Services/HallucinationGuardService.cs`  
**阶段**：Phase 2

---

## 幻觉的定义（在 RAG 语境下）

RAG 里的幻觉特指：**LLM 生成了与检索到的 context 无关的内容**，即模型用自己训练数据里的"知识"编造了一个听起来合理但不在知识库里的答案。

区别于一般意义的"LLM 幻觉"（无中生有），RAG 幻觉更隐蔽——答案可能是事实上正确的，但不来自用户的知识库，这在合规场景（医疗、法律、金融）是不可接受的。

---

## 第一层：Answer Embedding Check（向量相似度校验）

**逻辑**：对 LLM 生成的完整回答做 Embedding，然后在向量库里检索与它最相似的 chunk。

```csharp
// QueryService.cs
var answerEmbedding = await embeddingService.GenerateEmbeddingAsync(answer, ct);
var answerCheck = await vectorStore.SearchAsync(answerEmbedding, topK: 1, minSimilarity: 0f, ct: ct);
var maxAnswerSimilarity = answerCheck.Count > 0 ? answerCheck[0].Similarity : 0f;
var isHallucination = maxAnswerSimilarity < options.Value.HallucinationSimilarityThreshold; // 默认 0.3
```

**原理**：如果回答确实基于知识库内容，它的语义向量应该和库里的 chunk 向量高度接近。如果相似度 < 0.3，说明回答的语义"游离"在知识库之外。

**优点**：零额外 LLM 调用，成本极低（只多一次 Embedding + 向量查询）。  
**局限**：是统计判断，不是逻辑判断。高相似度不等于没有幻觉，只是降低了风险。

---

## 第二层：LLM Self-Check（自我校验）

**逻辑**：把原始 context 和 LLM 的回答一起发给 LLM，让它判断回答是否完全有据可查。

```csharp
// HallucinationGuardService.cs — System Prompt 要求只返回 true/false
var response = await chatService.CompleteAsync(SystemPrompt, userMessage, ct);
return response.Trim().StartsWith("true", StringComparison.OrdinalIgnoreCase);
```

**优点**：语义级判断，能识别"答案逻辑正确但来源错误"的幻觉。  
**成本**：额外一次完整 LLM 调用（慢且贵），通过 `EnableSelfCheckGuard: false` 默认关闭，按需启用。

---

## 两层的互补关系

| | 第一层（Embedding Check） | 第二层（Self-Check） |
|---|---|---|
| 成本 | 极低（1次 Embedding） | 高（1次 LLM 调用） |
| 速度 | 毫秒级 | 秒级 |
| 判断方式 | 统计（向量距离） | 语义（LLM 理解） |
| 默认状态 | 始终开启 | 关闭，可配置 |
| 适用场景 | 生产环境快速过滤 | 高合规要求场景 |

设计原则同双层去重：**先用廉价的统计方法快速过滤，只在必要时启用昂贵的 LLM 判断**。

---

## 结果的处理方式

幻觉检测结果不是"拦截回答"，而是**标记并透传给前端**：

```csharp
return new RagQueryResponse
{
    Answer = answer,
    IsHallucination = isHallucination,  // 前端显示 ⚠ 标记
    AnswerConfidence = results.Max(r => r.Similarity)
};
```

这个设计是刻意的：拦截会让用户完全看不到答案，但有时"可能有幻觉的回答"仍然有参考价值。**让用户自行决策**，而不是系统替用户决定"这个答案不该给你看"。

---

## 面试延伸点

- RAG 幻觉 vs. 一般 LLM 幻觉的区别：为什么 RAG 系统需要专门的幻觉检测
- 向量相似度作为幻觉代理指标的局限性：高相似度 ≠ 无幻觉（自我引用陷阱）
- 标记 vs. 拦截的产品决策：置信度/幻觉信号应该透出给用户，而不是在后端静默丢弃
- 与 RAG 推理边界的关系：见 [rag-prompt-boundary.md](rag-prompt-boundary.md)
