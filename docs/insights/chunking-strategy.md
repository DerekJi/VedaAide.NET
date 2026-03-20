# 动态分块策略：为什么不用固定大小分块

**项目**：VedaAide.NET  
**涉及文件**：`src/Veda.Core/ChunkingOptions.cs`、`src/Veda.Core/DocumentType.cs`  
**阶段**：Phase 1

---

## 背景

RAG 的入门实现通常按固定 token 数（如 512）切割所有文档。这个做法简单，但对不同类型的内容效果差异巨大。

---

## 核心洞察：文档类型决定最优颗粒度

不同文档的**语义密度**和**检索目标**不同：

| 文档类型 | 内容特征 | 检索目标 | 最优颗粒度 |
|---|---|---|---|
| 账单/Invoice | 每行包含独立的金额、日期、项目 | 精确提取某一字段 | 小（256 token） |
| 规范/PDS | 条款之间高度相关，割断会丢失上下文 | 理解完整条款语义 | 大（1024 token） |
| 报告/个人笔记 | 段落相对独立，中等关联 | 主题检索 | 中（512 token） |

如果把账单按 512 token 切割，一个 chunk 里可能混入多张账单的数据，回答"3月份的水费是多少"时极易检索到错误的数字。如果把规范文档按 256 token 切割，一个条款被截断，LLM 拿到的是残缺语义，回答质量下降。

---

## 实现方式

在 `DocumentType` 枚举 + `ChunkingOptions` 的工厂方法里集中管理：

```csharp
// ChunkingOptions.cs
public static ChunkingOptions ForDocumentType(DocumentType type) => type switch
{
    DocumentType.BillInvoice   => new(256,  32),   // 小颗粒，少重叠
    DocumentType.Specification => new(1024, 128),  // 大颗粒，多重叠
    DocumentType.Report        => new(512,  64),
    DocumentType.PersonalNote  => new(256,  32),   // 个人笔记同账单，短而精
    _                          => new(512,  64)
};
```

`TokenSize` 是 chunk 的主体大小，`OverlapTokens` 是相邻 chunk 之间的滑动窗口重叠量——重叠的作用是防止语义边界恰好落在切割点，避免"一句话被切成两半"导致两个 chunk 都无法独立表达完整含义。

---

## Overlap（重叠）的取舍

重叠越大，检索命中率越高，但：
- 存储空间增加（重复存了相同 token）
- 去重逻辑需要更高的相似度阈值才能识别近似重复块

当前选择 `overlap = chunkSize / 8`（约 12.5%），是业界经验值的下限，适合本地存储不紧张的场景。

---

## DocumentType 的自动检测 vs. 用户指定

目前支持两种方式：
1. **用户在摄取时显式指定**（API 的 `documentType` 字段）
2. **未指定时默认 `Other`**（512 token 中等颗粒）

Phase 4 可以加自动分类：用文件名/内容特征（关键词、结构）推断 DocumentType，无需用户指定。

---

## 面试延伸点

- 固定分块 vs. 语义分块 vs. 基于文档类型的动态分块——三种策略的取舍
- Overlap 的作用：为什么 RAG 分块不是简单的"按长度切"
- 分块策略对 Embedding 质量的影响：chunk 太短则语义不完整，太长则向量被稀释、检索精度下降
- `DocumentType` 作为领域概念放在 `Veda.Core` 而不是 `Veda.Api`：为什么领域知识不应该依附于 API 层
