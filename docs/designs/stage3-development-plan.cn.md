# VedaAide 三期开发计划与实施路线

> 基准文档：[能力差距分析](./stage2-gap-analysis.cn.md)、[二期开发计划](./stage2-development-plan.cn.md)、[系统设计](./system-design.cn.md)
>
> 形成背景：以二期差距分析中未覆盖或仅部分满足的缺口为输入，聚焦平台从"通用问答型 AI"走向"可信知识推理平台"的核心能力建设。

**文档目标**：定义三期开发范围、模块设计、实施路线及验收标准，作为工程落地的参考基线。

---

## 1. 背景与目标

### 1.1 二期产出基础

VedaAide 二期（Stage 2）已完成：

- 云端部署（Azure Container Apps + CosmosDB + Azure OpenAI）
- 双模式 LLM 路由（GPT-4o-mini / DeepSeek）
- 语义缓存（CosmosDB 向量相似度命中）
- Embedding 提供商配置化切换
- API Key 认证 + Rate Limiting + CORS + Managed Identity
- MCP 外部客户端支持（简历网站集成）
- DB Admin 开发工具

### 1.2 三期目标

三期以差距分析中 P0/P1/P2 缺口为主轴，系统性补齐"内容摄取质量"、"检索精度"、"推理可解释性"、"知识演化"和"个性化学习"五个维度的能力：

| 目标 | 优先级 | 说明 |
|------|--------|------|
| **富格式文档摄取** | P0 | 版式感知分段、表格结构化提取、OCR 回退，覆盖扫描件/手写/表格密集型 PDF |
| **可插拔语义增强层** | P0 | 个人词库接口、查询扩展、别名索引，无需修改核心代码 |
| **上下文感知检索路由** | P0 | `KnowledgeScope` 元数据模型 + 多维度过滤 + 偏好排序 |
| **混合检索双通道** | P1 | 独立 BM25 倒排通道 + 向量通道召回融合，提升精确词汇匹配准确率 |
| **知识版本化与变更追踪** | P1 | 文档版本字段、`DocumentDiffService`、版本感知同步、缓存自动失效 |
| **结构化推理输出** | P1 | `FindingType` / `Evidence[]` / `Confidence` 协议，条目级 Citation |
| **隐式反馈学习** | P2 | 行为事件采集 → 用户级私有记忆层 → 个性化检索权重更新 |
| **多用户知识治理** | P2 | 个人/共享/共识/公共四层治理，严格隐私隔离 |

---

## 2. 新增模块架构

在二期架构基础上，于 `Veda.Services` 与 `Veda.Storage` 之间增加**平台能力扩展层**，各模块均为通用能力，不绑定具体用户或场景：

```
┌─────────────────────────────────────────────────────────┐
│                     Veda.Api                            │
├─────────────────────────────────────────────────────────┤
│                   Veda.Agents                           │
├─────────────────────────────────────────────────────────┤
│                   Veda.Services                         │
│  QueryService / DocumentIngestService / ...             │
├───────────────────────────────────── 三期新增 ──────────┤
│  Veda.Ingest.Layout   │  版式感知文档解析                 │
│  Veda.Semantics       │  可插拔语义增强                   │
│  Veda.Knowledge.Scope │  知识作用域、路由、版本化           │
│  Veda.Output.Structured│ 结构化推理输出协议               │
│  Veda.Feedback        │  行为事件采集、个性化权重           │
│  Veda.Governance      │  多用户知识治理                   │
├─────────────────────────────────────────────────────────┤
│  Veda.Storage  (CosmosDB / SQLite)                      │
└─────────────────────────────────────────────────────────┘
```

---

## 3. 功能模块详细设计

### 3.1 富格式文档摄取（`Veda.Ingest.Layout`）

**痛点**：现有摄取管线以纯文本为主，复杂版式文档（表格、多列、手写注释、扫描件）切片后语义边界破碎。

#### 摄取配置档案（Ingestion Profile）

为不同文档类型定义摄取策略：

```csharp
public enum IngestProfile
{
    PlainText,        // 默认：段落切分
    LayoutAware,      // 版式感知：标题层级 + 列区 + 注释区识别
    TableExtraction,  // 表格结构化：行列关系保留为 JSON/Markdown 表格
    OcrFallback       // OCR 回退：扫描件 PDF / 手写内容
}

public class DocumentIngestOptions
{
    public IngestProfile Profile { get; set; } = IngestProfile.PlainText;
    public bool EnableTableExtraction { get; set; } = false;
    public bool EnableOcrFallback { get; set; } = false;
    public int MaxChunkTokens { get; set; } = 512;
}
```

#### 关键接口

```csharp
// 新增于 Veda.Core.Interfaces
public interface ILayoutParser
{
    // 从原始文档字节流中提取结构化内容（标题树、正文块、表格、注释）
    Task<ParsedDocument> ParseAsync(
        byte[] content, string mimeType, DocumentIngestOptions options,
        CancellationToken ct = default);
}

public record ParsedDocument(
    IReadOnlyList<ContentBlock> Blocks,
    IReadOnlyList<ExtractedTable> Tables,
    IReadOnlyList<string> CrossReferences);

public record ContentBlock(string Text, BlockType Type, int PageNumber, int HeadingLevel);
public record ExtractedTable(string MarkdownTable, string Caption, int PageNumber);
public enum BlockType { Heading, Paragraph, ListItem, Caption, Annotation }
```

#### 集成点

- `DocumentIngestService.IngestAsync` →  根据文件扩展名/MIME 类型自动选择 `IngestProfile`，或由调用方显式传入
- 表格提取结果序列化为 Markdown 表格格式后作为独立 chunk，保留行列上下文
- OCR 回退通道通过可选依赖（`Tesseract` 或 Azure AI Document Intelligence）注入，不强依赖

#### 新增配置项

```json
{
  "Veda": {
    "Ingest": {
      "DefaultProfile": "PlainText",
      "EnableTableExtraction": false,
      "EnableOcrFallback": false,
      "OcrProvider": "AzureDocumentIntelligence",
      "AzureDocumentIntelligence": {
        "Endpoint": "",
        "ApiKey": ""
      }
    }
  }
}
```

---

### 3.2 可插拔语义增强层（`Veda.Semantics`）

**痛点**：系统语义理解完全依赖通用 embedding，无法识别用户个性化词汇、缩写和私有标签体系。

#### 语义增强扩展点设计

```csharp
// 新增于 Veda.Core.Interfaces
public interface ISemanticEnhancer
{
    // 查询扩展：将缩写/自定义词汇映射到规范化同义词集合
    Task<string> ExpandQueryAsync(string query, CancellationToken ct = default);

    // 别名注入：在切片索引阶段为 chunk 添加用户自定义别名标签
    Task<IReadOnlyList<string>> GetAliasTagsAsync(
        string content, CancellationToken ct = default);
}

// 配置文件驱动的个人词库实现
public class PersonalVocabularyEnhancer : ISemanticEnhancer
{
    // 词库来源：JSON 配置文件 / 用户 API 上传，不耦合核心代码
}
```

#### 个人词库格式（JSON 配置）

```json
{
  "vocabulary": [
    { "term": "bg", "synonyms": ["背景资料", "背景文档", "context"] },
    { "term": "gy", "synonyms": ["工作总结", "工作汇报"] },
    { "term": "Q4", "synonyms": ["第四季度", "四季度"] }
  ],
  "tags": [
    { "pattern": "血压|血糖|体检", "labels": ["健康", "医疗档案"] },
    { "pattern": "预算|支出|收入", "labels": ["财务", "账单"] }
  ]
}
```

#### 集成点

- `QueryService.QueryAsync` → 在生成 embedding 前调用 `ISemanticEnhancer.ExpandQueryAsync`
- `DocumentIngestService.IngestAsync` → chunk 入库时调用 `ISemanticEnhancer.GetAliasTagsAsync`，将别名作为 `metadata.aliasTags` 存储
- `ISemanticEnhancer` 默认实现为 `NoOpSemanticEnhancer`（透传），词库功能通过配置启用
- 词库文件路径通过 `Veda:Semantics:VocabularyFilePath` 配置，由用户独立提供

---

### 3.3 上下文感知检索路由（`Veda.Knowledge.Scope`）

**痛点**：所有检索统一打入同一知识库，无法按知识来源类型、生活领域、时间范围等维度缩小检索范围。

#### KnowledgeScope 元数据模型

扩展 `VectorChunks` 容器中每个 chunk 的元数据：

```json
{
  "metadata": {
    "scope": {
      "sourceType": "HealthRecord",
      "domain": "Health",
      "tags": ["体检", "血压"],
      "validFrom": "2024-01-01",
      "validTo": null,
      "ownerId": "user-guid",
      "visibility": "Private"
    }
  }
}
```

#### 检索路由接口

```csharp
public record KnowledgeScope(
    string? Domain = null,
    string? SourceType = null,
    IReadOnlyList<string>? Tags = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    string? OwnerId = null,
    Visibility Visibility = Visibility.Private);

public enum Visibility { Private, Shared, Public }

// 新增于查询请求
public record RagQueryRequest(
    string Question,
    QueryMode Mode = QueryMode.Simple,
    KnowledgeScope? Scope = null);     // 可选：不传则不过滤
```

#### 路由模式

| 模式 | 说明 |
|------|------|
| **精确过滤（must-match）** | `domain=Health` 只召回健康领域文档 |
| **偏好排序（boost-by-scope）** | 优先排序匹配 scope 的结果，但不排除其他内容 |

路由逻辑在 `IVectorStore.SearchAsync` 中通过附加 CosmosDB WHERE 子句实现，不影响向量索引本身。

---

### 3.4 混合检索双通道（`Veda.Knowledge.Scope` 的检索层）

**痛点**：纯向量检索对精确词汇（人名、日期、自定义标签、编号）存在语义模糊，精确项目可能被排在后面。

#### 双通道召回架构

```
Query
  ├─ [向量通道] embedding → CosmosDB DiskANN → top-K 向量候选
  └─ [关键词通道] BM25 / 倒排索引 → top-K 关键词候选
        ↓
   [融合层] RRF（Reciprocal Rank Fusion）或加权合并
        ↓
   [Reranker] CrossEncoder Rerank（已有）
        ↓
   Top-N 最终结果
```

#### 关键接口扩展

```csharp
// 扩展现有 IVectorStore
public interface IVectorStore
{
    Task<IReadOnlyList<DocumentChunk>> SearchAsync(
        float[] queryEmbedding, int topK,
        KnowledgeScope? scope = null,
        CancellationToken ct = default);

    // 新增：关键词检索通道
    Task<IReadOnlyList<DocumentChunk>> SearchByKeywordsAsync(
        string query, int topK,
        KnowledgeScope? scope = null,
        CancellationToken ct = default);
}

// 新增融合服务
public interface IHybridRetriever
{
    Task<IReadOnlyList<DocumentChunk>> RetrieveAsync(
        string query, float[] queryEmbedding, int topK,
        HybridRetrievalOptions options,
        CancellationToken ct = default);
}

public record HybridRetrievalOptions(
    float VectorWeight = 0.7f,
    float KeywordWeight = 0.3f,
    FusionStrategy Strategy = FusionStrategy.Rrf);

public enum FusionStrategy { Rrf, WeightedSum }
```

#### 新增配置项

```json
{
  "Veda": {
    "Rag": {
      "HybridRetrievalEnabled": true,
      "VectorWeight": 0.7,
      "KeywordWeight": 0.3,
      "FusionStrategy": "Rrf"
    }
  }
}
```

**关键词通道实现**：CosmosDB for NoSQL 支持全文检索（`CONTAINS` / 全文索引），可作为 BM25 的平替；如需真正 BM25，可引入 Azure AI Search 作为独立关键词索引（可选依赖）。

---

### 3.5 知识版本化与变更追踪（`Veda.Knowledge.Scope`）

**痛点**：知识库是静态快照，文档更新时整体替换，无历史版本、无变更追踪。

#### 版本字段扩展

在 `VectorChunks` 容器中增加版本字段：

```json
{
  "version": 3,
  "validFrom": "2026-03-25T00:00:00Z",
  "supersededBy": null,
  "previousVersionId": "chunk-guid-v2",
  "changeType": "Updated"
}
```

#### 新增服务

```csharp
// 新增于 Veda.Core.Interfaces
public interface IDocumentDiffService
{
    // 对比新旧版本文档，生成结构化变更摘要
    Task<DocumentChangeSummary> DiffAsync(
        string documentId, string oldContent, string newContent,
        CancellationToken ct = default);
}

public record DocumentChangeSummary(
    string DocumentId,
    int AddedChunks,
    int RemovedChunks,
    int ModifiedChunks,
    IReadOnlyList<string> ChangedTopics,
    DateTimeOffset ChangedAt);
```

#### 集成点

- `DocumentIngestService.IngestAsync` → 入库前检测文档是否已存在：
  - 已存在且内容变更：调用 `IDocumentDiffService.DiffAsync`，旧 chunk 标记 `supersededBy`，新 chunk 写入
  - 已存在且内容未变：跳过（现有 Hash 比对已实现此路径）
- `ISemanticCache.InvalidateByDocumentAsync` → 版本更新后自动触发缓存失效（二期已预留此接口）
- `GET /api/admin/documents/{documentId}/history` → 新增端点，返回版本历史

---

### 3.6 结构化推理输出（`Veda.Output.Structured`）

**痛点**：系统输出为自由文本 + 来源列表，缺少机器可处理的结构化推理协议，无法支持可审计的个人决策场景。

#### 结构化输出协议

```csharp
// 新增于 Veda.Core
public record StructuredFinding(
    FindingType Type,
    string Summary,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<EvidenceItem>? CounterEvidence,
    double Confidence,
    string? UncertaintyNote);

public enum FindingType { Information, Warning, Conflict, HighRisk }

public record EvidenceItem(
    string DocumentId,
    string DocumentName,
    string Snippet,
    double RelevanceScore);

// 扩展现有 RagQueryResponse
public record RagQueryResponse(
    string Answer,
    IReadOnlyList<DocumentSource> Sources,
    StructuredFinding? StructuredOutput = null);    // 可选，按请求参数启用
```

#### API 变更

```http
POST /api/query
{
  "question": "...",
  "mode": "advanced",
  "structuredOutput": true    // 新增可选参数，默认 false
}
```

#### 集成点

- `QueryService.QueryAsync` → 当 `structuredOutput=true` 时，调用专用 Prompt 模板（`Veda.Prompts` 中新增结构化输出模板）强制 LLM 按协议格式返回
- GraphQL Schema 同步扩展，暴露 `structuredOutput` 字段
- `Veda.Evaluation` → 新增结构化输出可解释度指标（Evidence 覆盖率、Confidence 校准度）

---

### 3.7 隐式反馈学习（`Veda.Feedback`）

**痛点**：知识库权重完全静态，用户的隐式反馈（标记无关结果、修改摘要、采纳推荐）没有被利用。

#### 行为事件模型

```csharp
// 新增于 Veda.Core
public record UserBehaviorEvent(
    string UserId,
    string SessionId,
    BehaviorType Type,
    string? RelatedDocumentId,
    string? RelatedChunkId,
    string? Query,
    DateTimeOffset OccurredAt,
    IDictionary<string, object>? Payload = null);

public enum BehaviorType
{
    ResultAccepted,      // 用户采纳了推荐结果
    ResultRejected,      // 用户标记结果无关
    AnswerEdited,        // 用户修改了 AI 输出
    SourceClicked,       // 用户点击了来源链接
    QueryRefined         // 用户细化了查询（追问）
}
```

#### 用户级私有记忆层

```csharp
// 新增于 Veda.Core.Interfaces
public interface IUserMemoryStore
{
    // 记录行为事件
    Task RecordEventAsync(UserBehaviorEvent evt, CancellationToken ct = default);

    // 获取用户对特定文档/chunk 的权重偏好（基于历史行为聚合）
    Task<float> GetBoostFactorAsync(
        string userId, string chunkId, CancellationToken ct = default);

    // 获取用户的个性化术语偏好（行为驱动的词汇扩展补充）
    Task<IReadOnlyDictionary<string, float>> GetTermPreferencesAsync(
        string userId, CancellationToken ct = default);
}
```

#### 集成点

- `QueryService.QueryAsync` → Rerank 阶段之后，对有历史正向反馈的 chunk 施加 boost 因子
- API 新增 `POST /api/feedback` 端点，接收前端行为事件上报
- 行为事件存储于 CosmosDB 新容器 `UserBehaviors`（Partition Key：`/userId`）
- 提供可观测性端点 `GET /api/admin/feedback/stats`：哪些知识被频繁修正？哪些术语存在持续歧义？

#### 新增 CosmosDB 容器

```json
// 容器 3：UserBehaviors
{
  "id": "event-guid",
  "userId": "user-guid",
  "sessionId": "session-guid",
  "type": "ResultRejected",
  "relatedChunkId": "chunk-guid",
  "query": "...",
  "occurredAt": "2026-03-25T00:00:00Z"
}
```

- **Partition Key**：`/userId`
- **TTL**：90 天（行为数据不需要永久保留）

---

### 3.8 多用户知识治理（`Veda.Governance`）

**痛点**：每位用户的知识孤岛独立，无跨用户知识学习与聚合发布机制，也无多层级的隐私隔离框架。

#### 四层知识治理模型

| 层级 | 范围 | 可见性 | 管理方式 |
|------|------|--------|----------|
| **个人私有层** | 仅该用户 | Private | 用户完全自主管理 |
| **家庭/团队共享层** | 用户主动授权的成员 | Shared（授权范围内） | 用户授权 + 成员确认 |
| **共识候选层** | 匿名聚合后达阈值的通用模式 | 仅管理员可见 | 系统自动候选 + 人工审核 |
| **平台公共层** | 所有用户 | Public | 审核通过后发布 |

#### 关键接口

```csharp
// 新增于 Veda.Core.Interfaces
public interface IKnowledgeGovernanceService
{
    // 创建共享组（家庭/团队）
    Task<string> CreateSharingGroupAsync(
        string ownerId, IReadOnlyList<string> memberIds,
        CancellationToken ct = default);

    // 提名共识候选（系统自动触发，匿名聚合）
    Task NominateConsensusAsync(
        string anonymizedPattern, double supportRatio,
        CancellationToken ct = default);

    // 审核共识候选（管理员操作）
    Task<bool> ReviewConsensusAsync(
        string candidateId, bool approved,
        CancellationToken ct = default);
}
```

#### 隐私保护要求（强制约束）

- 所有跨用户操作**严格匿名化**，个人原始内容绝不参与聚合
- 共识候选仅基于行为模式统计（"哪类问题被普遍认为无关"），不包含任何文档内容
- 共享层文档必须经用户明确授权（`PUT /api/documents/{id}/share`）才可跨用户可见
- 加入审核、回滚和污染隔离机制，防止错误共识扩散

---

## 4. 新增 / 修改模块汇总

### 新增

| 模块 / 文件 | 说明 |
|-------------|------|
| `Veda.Ingest.Layout/LayoutParser.cs` | 实现 `ILayoutParser`，版式感知分段 |
| `Veda.Ingest.Layout/TableExtractor.cs` | 表格结构化提取，输出 Markdown 表格格式 |
| `Veda.Ingest.Layout/OcrFallbackParser.cs` | OCR 回退通道（可选依赖） |
| `Veda.Ingest.Layout/IngestProfileSelector.cs` | 根据 MIME 类型自动选择摄取配置档案 |
| `Veda.Semantics/PersonalVocabularyEnhancer.cs` | 配置文件驱动的个人词库语义增强 |
| `Veda.Semantics/NoOpSemanticEnhancer.cs` | 默认透传实现 |
| `Veda.Knowledge.Scope/KnowledgeScopeFilter.cs` | KnowledgeScope 过滤条件构建与应用 |
| `Veda.Knowledge.Scope/HybridRetriever.cs` | 实现 `IHybridRetriever`，双通道召回融合 |
| `Veda.Knowledge.Scope/DocumentDiffService.cs` | 实现 `IDocumentDiffService`，文档版本对比 |
| `Veda.Output.Structured/StructuredOutputParser.cs` | LLM 结构化输出解析与验证 |
| `Veda.Output.Structured/StructuredPromptTemplates.cs` | 结构化推理专用 Prompt 模板 |
| `Veda.Feedback/UserBehaviorCollector.cs` | 行为事件采集与存储 |
| `Veda.Feedback/UserMemoryStore.cs` | 实现 `IUserMemoryStore`，CosmosDB 持久化 |
| `Veda.Feedback/FeedbackBoostService.cs` | 基于历史反馈计算 chunk boost 因子 |
| `Veda.Governance/KnowledgeGovernanceService.cs` | 实现 `IKnowledgeGovernanceService` |
| `Veda.Governance/ConsensusCandidateStore.cs` | 共识候选 CosmosDB 存储 |
| `Veda.Storage/CosmosDbUserBehaviorStore.cs` | UserBehaviors 容器访问层 |
| `Veda.Api/Controllers/FeedbackController.cs` | `POST /api/feedback` 端点 |
| `Veda.Api/Controllers/GovernanceController.cs` | 知识治理管理端点 |

### 修改（现有文件）

| 文件 | 变更内容 |
|------|----------|
| `Veda.Core/Interfaces/IVectorStore.cs` | 新增 `SearchByKeywordsAsync`；`SearchAsync` 增加 `KnowledgeScope?` 参数 |
| `Veda.Core/DocumentChunk.cs` | 新增 `version`、`validFrom`、`supersededBy`、`scope` 字段 |
| `Veda.Core/RagQueryRequest.cs` | 新增 `Scope`、`StructuredOutput` 参数 |
| `Veda.Core/RagQueryResponse.cs` | 新增 `StructuredOutput` 字段（可选） |
| `Veda.Services/QueryService.cs` | 集成 `IHybridRetriever`、`ISemanticEnhancer`、`IUserMemoryStore` boost |
| `Veda.Services/DocumentIngestService.cs` | 集成 `ILayoutParser`、`ISemanticEnhancer`、`IDocumentDiffService` |
| `Veda.Storage/CosmosDbVectorStore.cs` | 实现关键词检索通道；`SearchAsync` 支持 `KnowledgeScope` 过滤 |
| `Veda.Api/Controllers/AdminController.cs` | 新增文档版本历史端点、反馈统计端点 |
| `Veda.Api/Program.cs` | 注册三期新服务 |
| `src/Veda.Api/appsettings.json` | 新增 `Ingest`、`Semantics`、`Rag.HybridRetrieval` 配置节 |
| `Veda.Evaluation/EvaluationMetrics.cs` | 新增摄取完整性、Citation 准确率、结构化输出可解释度指标 |
| `docs/configuration/configuration.cn.md` | 补充三期新增配置项说明 |

---

## 5. 实施路线

### Sprint 1（2 周）：检索链路升级

**目标**：精确词汇查询准确率显著提升；KnowledgeScope 可用。

- [ ] `KnowledgeScope` 元数据模型设计与 CosmosDB schema 扩展
- [ ] `IVectorStore.SearchAsync` 支持 `KnowledgeScope` 过滤（WHERE 子句）
- [ ] `SearchByKeywordsAsync` 实现（CosmosDB 全文检索）
- [ ] `IHybridRetriever` + `HybridRetriever` 实现（RRF 融合策略）
- [ ] `QueryService` 集成 `IHybridRetriever`
- [ ] `RagQueryRequest` 新增 `Scope` 参数，API 透传
- [ ] 新增混合检索配置项（`HybridRetrievalEnabled` / `VectorWeight` / `KeywordWeight`）
- [ ] 现有文档重新标注 `KnowledgeScope` 元数据（迁移脚本）

**验收**：
- 精确词汇查询（人名、日期、编号）召回准确率通过评估体系验证有明显提升
- 按 `domain=Health` 过滤，只返回健康类文档
- 混合检索双通道权重可通过配置调整

---

### Sprint 2（2 周）：摄取质量升级

**目标**：复杂文档摄取后语义完整性明显改善。

- [ ] `ILayoutParser` 接口定义
- [ ] `LayoutParser` 实现（标题层级、段落、注释区识别）
- [ ] `TableExtractor` 实现（表格 → Markdown 格式 chunk）
- [ ] `IngestProfileSelector` 实现（按 MIME 类型自动选择）
- [ ] `DocumentIngestService` 集成 `ILayoutParser`
- [ ] `OcrFallbackParser` 实现（Azure Document Intelligence，可选依赖，通过 DI 注入）
- [ ] 新增摄取完整性评估指标（`Veda.Evaluation`）
- [ ] 新增摄取配置项（`Veda:Ingest:DefaultProfile` / `EnableTableExtraction`）

**验收**：
- 含表格的 PDF 摄取后，表格中每个单元格的行列关系可在 chunk 中正确还原
- 标题层级在切片时作为上下文保留（chunk 包含面包屑路径）
- 摄取完整性指标可在 `Veda.Evaluation` 中量化

---

### Sprint 3（2 周）：结构化输出 + 版本化 + 语义增强

**目标**：输出可审计；知识可追溯演化；个人词库可接入。

- [ ] `StructuredFinding` / `EvidenceItem` 数据模型定义
- [ ] 结构化输出 Prompt 模板（`Veda.Prompts`）
- [ ] `StructuredOutputParser` 实现（解析 + 校验 LLM 结构化输出）
- [ ] `QueryService` 集成结构化输出路径（`structuredOutput=true` 时启用）
- [ ] GraphQL Schema 扩展暴露 `structuredOutput` 字段
- [ ] `DocumentChunk` 新增版本字段（schema 迁移）
- [ ] `IDocumentDiffService` + `DocumentDiffService` 实现
- [ ] `DocumentIngestService` 集成版本化逻辑（旧 chunk 标记 `supersededBy`）
- [ ] `GET /api/admin/documents/{documentId}/history` 端点
- [ ] `ISemanticEnhancer` 接口 + `PersonalVocabularyEnhancer` + `NoOpSemanticEnhancer` 实现
- [ ] `QueryService` 集成 `ISemanticEnhancer.ExpandQueryAsync`
- [ ] `DocumentIngestService` 集成 `ISemanticEnhancer.GetAliasTagsAsync`
- [ ] 词库配置文件格式文档化

**验收**：
- `structuredOutput=true` 时每个结论含 `Evidence[]` 支撑，无证据时 `Confidence` < 0.5
- 更新文档后，旧版 chunk 被标记 `supersededBy`，历史端点可返回版本列表
- 配置个人词库后，用缩写查询可命中对应文档（评估集验证）

---

### Sprint 4（2 周）：反馈学习 + 知识治理

**目标**：用户行为可采集并影响检索排序；跨用户知识严格隔离。

- [ ] `UserBehaviorEvent` 模型 + `UserBehaviors` CosmosDB 容器
- [ ] `IUserMemoryStore` + `UserMemoryStore` 实现
- [ ] `FeedbackBoostService` 实现（历史反馈 → chunk boost 因子）
- [ ] `QueryService` 集成 `FeedbackBoostService`（Rerank 后 boost）
- [ ] `FeedbackController`（`POST /api/feedback`）
- [ ] `GET /api/admin/feedback/stats` 端点
- [ ] `IKnowledgeGovernanceService` + `KnowledgeGovernanceService` 实现
- [ ] `PUT /api/documents/{id}/share` 端点（授权共享）
- [ ] 共识候选匿名聚合逻辑（仅基于行为统计，无内容泄露）
- [ ] `GovernanceController`（审核端点）
- [ ] 隐私隔离测试：验证跨用户 `ownerId` 过滤严格生效

**验收**：
- 行为事件可被采集，正向反馈的 chunk 在后续同类查询中排序提升
- 用户 A 的私有文档在用户 B 的查询中绝对不返回（隐私隔离测试通过）
- 共享文档需经授权方确认，未授权时不跨用户可见
- 反馈统计端点可展示"高频被标记无关"的 chunk 列表

---

## 6. 关键验收标准汇总

| 功能 | 验收标准 |
|------|----------|
| 混合检索双通道 | 精确词汇（人名/日期/编号）召回准确率通过评估体系验证提升 ≥ 10% |
| KnowledgeScope 路由 | 按 `domain` 过滤后，召回结果 100% 属于该 domain |
| 富格式摄取 | 含表格的 PDF 摄取后，表格行列关系在 chunk 中正确保留 |
| 语义增强层 | 个人词库通过配置文件接入，不修改任何核心代码 |
| 结构化推理输出 | 每个 `StructuredFinding` 均含 1 条以上 `Evidence`，`Confidence` 与结论强度一致 |
| 知识版本化 | 文档更新后，旧版 chunk 标记 `supersededBy`，历史端点返回版本链 |
| 文档 Diff | `DocumentDiffService` 可识别新增/删除/修改 chunk 并生成变更摘要 |
| 隐式反馈学习 | 正向反馈 5 次后，该 chunk 在同类查询中排序可量化提升 |
| 隐私隔离 | 用户 A 的 Private 文档在用户 B 查询中零泄露（自动化测试验证） |
| 共识发布 | 共识候选中无任何个人原始内容，审核通过后可查到平台公共层文档 |

---

## 7. 风险与注意事项

| 风险 | 应对策略 |
|------|----------|
| OCR 依赖成本 | Azure Document Intelligence 按页计费；默认关闭，仅对扫描件文档启用 |
| BM25 倒排与 CosmosDB 全文检索能力差距 | 先用 CosmosDB 全文索引实现关键词通道；如效果不足，引入 Azure AI Search（作为可选依赖，不强依赖） |
| 结构化输出 LLM 不稳定遵从 | Prompt 加强约束 + 响应解析校验；解析失败时降级为自由文本输出，不阻塞响应 |
| 版本化迁移对已有数据的影响 | 新版本字段设默认值，存量数据视为 `version=1`；迁移脚本幂等可重入 |
| 反馈数据冷启动（新用户无历史） | 无历史时 boost 因子默认 1.0（不影响排序），冷启动期间退化为无个性化检索 |
| 隐私设计优先级 | 隐私隔离（`ownerId` + `Visibility` 过滤）必须在 Sprint 4 第一个任务完成，其他治理功能依赖其验证通过后才可开发 |
| 评估体系扩展 | 三期新增指标（摄取完整性、Citation 准确率、Confidence 校准度）需在对应 Sprint 同步建立基线，避免无法量化验收 |

---

## 8. 与差距分析的对应关系

参考 [stage2-gap-analysis.cn.md](./stage2-gap-analysis.cn.md)：

| 差距分析项（编号） | 优先级 | 三期处理方式 |
|-------------------|--------|-------------|
| 3.1 富格式文档摄取 | P0 | ✅ Sprint 2：`Veda.Ingest.Layout`（版式感知 + 表格提取 + OCR） |
| 3.2 可插拔语义增强层 | P0 | ✅ Sprint 3：`Veda.Semantics`（个人词库接口 + 查询扩展 + 别名索引） |
| 3.3 上下文感知检索路由 | P0 | ✅ Sprint 1：`KnowledgeScope` 元数据 + 多维度过滤路由 |
| 3.4 结构化推理输出 | P1 | ✅ Sprint 3：`Veda.Output.Structured`（FindingType / Evidence[] / Confidence） |
| 3.5 知识版本化与变更追踪 | P1 | ✅ Sprint 3：版本字段 + `DocumentDiffService` + 版本感知同步 |
| 3.6 混合检索双通道 | P1 | ✅ Sprint 1：BM25 + 向量双通道召回融合（RRF） |
| 3.7 隐式反馈学习 | P2 | ✅ Sprint 4：`Veda.Feedback`（行为采集 + 私有记忆层 + 权重更新） |
| 3.8 多用户知识治理 | P2 | ✅ Sprint 4：`Veda.Governance`（四层模型 + 隐私隔离 + 共识候选） |

差距分析中识别的全部 8 项缺口均在三期覆盖，无遗漏。
