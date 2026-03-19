# 📁 项目设计草案：VedaAide .NET
 
## 1. 项目基本信息
 
- 项目名称：VedaAide .NET
- 核心定位：通用型、企业级、私有化 RAG 智能问答系统
- 技术栈（后端）：.NET 8 / 9 (C#) + ASP.NET Core + EF Core + Semantic Kernel + MCP + Microsoft Agent Framework
- 技术栈（AI）：Ollama (本地 Embedding) + DeepSeek / Azure OpenAI (LLM)
- 技术栈（前端）：Angular + TypeScript
- 技术栈（API）：GraphQL (HotChocolate) + REST
- 技术栈（存储）：SQLite-VSS (向量) + SQLite/SQL Server via EF Core (关系数据)
- 技术栈（云）：Azure Blob Storage + Azure OpenAI + Azure Container Apps
- 项目目标：通用的私有知识库，兼顾本地私有化与云端扩展
- 演示场景：外网可访问（本地服务器 + Cloudflare Tunnel / Azure Container Apps）
 
## 2. 核心架构设计 (Top Level Architecture)
 
采用本地+云端混合架构，最大化成本控制与隐私安全。
 
🏛️ 系统架构图 (逻辑层)
 
1. 前端层 (Frontend)
- Angular + TypeScript SPA
- 支持文档上传、对话式问答、结果展示、评估报告查看
- 通过 GraphQL (HotChocolate) 与后端通信
2. API 网关层 (API Gateway)
- ASP.NET Core Web API
- GraphQL endpoint (HotChocolate)
- REST endpoint（兼容性 / Webhook）
3. 核心业务层 (Core Business)
- RAG 引擎：文档处理、分块、检索、生成
- 去重引擎：基于哈希与相似度
- 防幻觉校验：向量校验 + LLM 自我校验
- Prompt Engineering 模块：提示模板管理、上下文窗口优化
- AI 评估模块：输出质量打分、模型间横向对比
4. Agent 编排层 (Agent Orchestration)
- Microsoft Agent Framework：多 Agent 协同工作流
- MCP (Model Context Protocol)：标准化工具调用与外部数据源接入
- Semantic Kernel：LLM 编排与 Plugin 体系
5. AI 服务层 (AI Services)
- 本地 Embedding：Ollama (bge-m3 / mxbai)
- 云端 LLM：DeepSeek Chat / Azure OpenAI
- Azure AI Search（可选，云端向量检索）
6. 数据存储层 (Data Storage)
- 向量库：SQLite-VSS (本地) / Azure AI Search (云端)
- 关系数据：EF Core + SQLite (本地) / SQL Server (云端扩展)
  - 存储：用户会话、文档元数据、评估记录、Prompt 模板版本
- 文档库：本地文件系统 / Azure Blob Storage
 
 
 
## 3. 详细技术路线 (Technical Roadmap)
 
 
🏗️ 阶段零：项目脚手架 (Solution Bootstrap)

1. 创建 .NET Solution 及所有项目工程：
   - `Veda.Core`、`Veda.Services`、`Veda.Storage`、`Veda.Prompts`
   - `Veda.Agents`、`Veda.Evaluation`、`Veda.Api`、`Veda.Web`
   - 测试项目：`Veda.*.Tests`
2. 配置 EF Core + `VedaDbContext`（Code-First，SQLite）：
   - 初始迁移：ChatSession、DocumentMeta、PromptTemplate 表。
3. 配置基础 DI 容器、配置文件（appsettings.json）、日志框架。
4. 搭建 `Veda.Api`（ASP.NET Core Web API）最小骨架，验证启动。


🚀 阶段一：基础 RAG 引擎搭建 (MVP)

1. 文档处理管道
   - 优先支持 Txt / Markdown，后续扩展 PDF / Word。
   - 动态分块策略：
     - 账单/Invoice 类：小颗粒度（256 token），精准提取金额/日期。
     - 规范/PDS 类：大颗粒度（1024 token），保留完整语义。
2. Embedding 策略
   - 部署 Ollama 本地模型：`nomic-embed-text`（轻量首选）或 `mxbai-embed-large`（更高精度）。
   - 通过 Semantic Kernel `OllamaTextEmbeddingGenerationService` 封装。
   - 关键点：零成本、无网络延迟、私密。
3. 向量存储
   - 使用 `sqlite-vec`（sqlite-vss 的继任者）+ `Microsoft.Data.Sqlite`。
   - 封装为 `VectorDbProvider`，接口化便于切换。
4. RAG 查询端到端
   - `Veda.Api` 暴露两个 REST 端点：`POST /documents`（摄取）、`POST /query`（问答）。
   - Semantic Kernel 编排：检索相关块 → 构建提示 → 调用 Ollama LLM → 返回答案。


🔒 阶段二：RAG 质量增强 (Dedup + Anti-Hallucination + Ranking)

这是项目最大亮点，体现产品级 AI 工程思维。

1. 智能去重模块（双层）
   - **第一层 — 哈希去重**（已在阶段一实现）：计算分块内容 SHA-256，已存在则跳过，防止完全相同内容重复入库。
   - **第二层 — 向量相似度去重**：在摄取阶段，对每个新分块先做向量检索；若与已存储内容余弦相似度 ≥ `SimilarityDedupThreshold`（默认 0.95），则视为近似重复，跳过存储。
     - 实现位置：`DocumentIngestService.IngestAsync`，在 Embedding 后、`UpsertBatchAsync` 前过滤。
     - 好处：防止语义重复内容污染检索结果，提升回答质量。

2. 双层防幻觉体系 (Dual Verification)
   - **第一层 — 向量相似度校验 (Answer Embedding Check)**
     - 逻辑：LLM 生成回答 → 对回答做 Embedding → 向向量库检索对比。
     - 判断：若回答向量与检索到的最高相似度 < `HallucinationSimilarityThreshold`（默认 0.3），则判定为潜在幻觉。
     - 结果：设置 `RagQueryResponse.IsHallucination = true`，同时保留答案供前端自行决策是否展示。
     - 实现位置：`QueryService.QueryAsync`，在 `CompleteAsync` 之后。
   - **第二层 — LLM 自我校验 (Self-Check)**
     - 逻辑：通过 `IHallucinationGuardService` 再次调用 LLM，逐句审核回答是否有文档依据。
     - Prompt 策略：给模型提供原始 context 和生成的回答，要求返回 `true/false` 及置信理由。
     - 实现位置：新增 `IHallucinationGuardService`（`Veda.Core.Interfaces`）+ `HallucinationGuardService`（`Veda.Services`）。
     - 注意：此层会额外消耗 1 次 LLM 调用，通过配置 `Veda:EnableSelfCheckGuard: true/false` 控制是否开启。

3. RAG 检索优化
   - **Reranking（重排序）**：初始检索 `2 × TopK` 个候选块，通过 `IRerankService` 接口对候选列表重新打分排序，取分数最高的 `TopK` 个作为最终上下文。Phase 2 使用基于关键词覆盖率的轻量重排策略（无额外 API 调用）；Phase 4 可替换为 cross-encoder 模型。
   - **时间范围元数据过滤**：`RagQueryRequest` 新增可选字段 `DateFrom`/`DateTo`；`IVectorStore.SearchAsync` 新增对应参数；`SqliteVectorStore` 在 `WHERE` 子句中过滤 `CreatedAtTicks` 范围。


🌐 阶段三：API 层 + 前端 (Full-Stack)

1. `Veda.Api` 完善
   - 接入 GraphQL（HotChocolate），替代 / 并行 REST。
   - 流式响应（Server-Sent Events）支持对话页实时输出。
2. `Veda.Web`（Angular + TypeScript）
   - 文档管理页：上传、摄取状态、删除。
   - 对话页：流式问答，显示引用来源与置信度。
   - Prompt 管理页：查看/编辑版本化模板。
3. 部署
   - Docker Compose 一键启动（API + Frontend + Ollama）。
   - 方案 A（低成本）：本地服务器 + Cloudflare Tunnel。
   - 方案 B（云端）：Azure Container Apps，自动扩缩容。


🤖 阶段四：Agentic Workflow + MCP 集成

1. Microsoft Agent Framework 多 Agent 编排
- 定义专职 Agent：DocumentAgent（文档处理）、QueryAgent（问答）、EvalAgent（评估）。
- Agent 之间通过消息总线协调，支持并行/串行任务流。
- 场景示例：用户上传合同 -> DocumentAgent 解析 -> QueryAgent 就绪 -> EvalAgent 自动评分。
2. MCP (Model Context Protocol) 工具集成
- 将外部数据源（文件系统、数据库、Azure Blob）封装为 MCP Tool。
- LLM 通过 MCP 按需调用工具，实现动态上下文注入。
- 便于后续扩展：接入日历、Web 搜索、ERP 系统等外部工具。
3. Prompt / Context Engineering 模块
- 维护版本化 Prompt 模板库（存入 EF Core 数据库）。
- 上下文窗口动态裁剪：根据 Token 预算选择最相关文档块。
- Chain-of-Thought 提示策略，提升复杂推理质量。


📊 阶段五：AI 评估体系 (Evaluation & Test Harness)

这是 JD 中特别强调的能力，也是区分高级工程师的关键。

1. 评估指标体系
- 忠实度 (Faithfulness)：回答是否仅依赖检索到的上下文。
- 答案相关性 (Answer Relevancy)：回答是否切题。
- 上下文召回率 (Context Recall)：相关文档块是否被检索到。
- BLEU / ROUGE：与参考答案的文本相似度（可选）。
2. 自动化 Test Harness
- 维护 Golden Dataset：标准问题集 + 预期答案。
- 每次模型/Prompt 变更后自动运行评估，输出对比报告。
- 支持 A/B 测试：同一问题对比不同模型或 Prompt 版本的得分。
3. 集成测试策略
- RAG Pipeline 端到端集成测试（NUnit）。
- AI 输出的确定性边界测试（验证幻觉防护是否生效）。
- 使用 Mock LLM 实现快速、低成本的单元测试。
 
 
 
## 4. 核心功能模块详解 (Core Modules)
 
 
📂 4.1. Veda.Core  - 核心契约
 
```csharp  
// 文档类型枚举，驱动动态分块策略
public enum DocumentType
{
    BillInvoice,    // 账单/发票 -> 小颗粒
    Specification,  // 规范/PDS -> 大颗粒
    Report,         // 报告 -> 中颗粒
    Other
}

// RAG 请求模型
public class RagQueryRequest
{
    public string Question { get; set; }
    public DocumentType TargetType { get; set; } // 绑定检索策略
}
```
 
🧠  Veda.Services  - AI 服务层
 
-  IOllamaEmbeddingService 
- 封装本地 Ollama API，负责文本向量化。
-  IDeepSeekChatService 
- 封装云端聊天模型，负责生成回答。
-  IHallucinationGuardService  (关键模块)
- 方法： Task<bool> VerifyAnswerAsync(string answer, VectorDbContext context); 
- 逻辑：
1. 对 Answer 做 Embedding。
2. 在 VectorDb 中检索。
3. 对比相似度，如果低于阈值则判定为幻觉。
 
🗄️  Veda.Storage  - 数据层
 
-  VectorDbProvider 
- 基于 SQLite-VSS 的封装。
- 支持 Insert, Search, Verify (为了 Answer 校验)。
- 支持 自动备份到 Azure Blob。
-  VedaDbContext  (EF Core)
- 管理关系型数据：用户会话、文档元数据、Prompt 模板版本、评估记录。
- Code-First 迁移，本地 SQLite / 云端 SQL Server 双支持。


🤖  Veda.Agents  - Agent 编排层

- 基于 Microsoft Agent Framework 定义多个专职 Agent：
  -  DocumentAgent ：负责文档摄取、分块、去重、入库。
  -  QueryAgent ：负责接收用户问题、检索、生成、防幻觉校验。
  -  EvalAgent ：负责对 QueryAgent 的输出进行质量评估、打分。
- Agent 注册为 Semantic Kernel Plugin，统一编排。
- MCP 工具注册（ Veda.MCP ）：
  - `FileSystemTool`：读取本地文档目录。
  - `BlobStorageTool`：读写 Azure Blob。
  - `VectorSearchTool`：封装向量检索为 MCP 标准接口。


📝  Veda.Prompts  - Prompt 工程层

-  PromptTemplateRepository ：从数据库加载版本化 Prompt 模板。
-  ContextWindowBuilder ：根据 Token 预算动态选取文档块，构建最优上下文。
-  ChainOfThoughtStrategy ：为复杂问题注入推理步骤引导。
- 支持按 DocumentType 和场景选择不同提示策略。


📊  Veda.Evaluation  - AI 评估层

-  EvaluationRunner ：加载 Golden Dataset，批量运行评估。
-  FaithfulnessScorer ：检查回答是否有文档依据（基于向量相似度 + LLM 判断）。
-  AnswerRelevancyScorer ：判断回答与问题的相关性。
-  ModelComparisonReport ：对比不同模型/Prompt 版本的评估得分，输出 Markdown 报告。
- 集成 NUnit 测试，CI/CD 中自动触发评估流程。


🌐  Veda.Web  - 前端层 (Angular)

- 技术栈：Angular + TypeScript，通过 GraphQL (HotChocolate) 与后端通信。
- 核心页面：
  - 文档管理页：上传、查看摄取状态、删除文档。
  - 对话页：流式问答界面，显示引用来源与置信度。
  - 评估报告页：查看历史评估结果、模型对比图表。
  - Prompt 管理页：查看/编辑 Prompt 模板版本。
 
 
 
## 5. 项目价值与亮点 (Value Proposition)
 
5.1. 技术全面：覆盖 JD 全部核心技术栈——.NET / ASP.NET Core / EF Core / Semantic Kernel / MCP / Microsoft Agent Framework / RAG / Prompt Engineering / Azure。
5.2. 架构先进：动态分块 + 双层防幻觉 + Agentic Workflow + MCP 工具调用，体现真实产品级 AI 工程思维。
5.3. 有评估体系：实现 AI Test Harness（Golden Dataset + 多维评估指标 + 模型横向对比），这是区分高级 AI 工程师的核心能力。
5.4. 全栈覆盖：Angular + TypeScript 前端 + GraphQL (HotChocolate) API + .NET 后端，体现端到端交付能力。
5.5. 云端就绪：Azure Container Apps + Azure OpenAI + Azure Blob，可随时从本地迁移到云端，契合 SaaS / Cloud-First 场景。
5.6. 成本极致：本地 Ollama 做 Embedding，几乎 0 向量化成本；只在生成时调用 LLM API，费用极低。
5.7. 业务通用：设计为通用 RAG 框架，可快速适配建筑（Buildxact 场景）、金融、法律等行业知识库。
 
 
 
## 6. 潜在问题与解决方案 (Risk Management)
 
6.1 问题：大文档处理慢。
- 方案：批量处理 + 异步任务队列（Background Service）。
6.2 问题：跨模型向量不兼容。
- 方案：`VectorChunkEntity` 记录每块向量生成时使用的模型名称（`EmbeddingModel` 字段，已在阶段二实现）；切换模型时通过 `WHERE EmbeddingModel != 'new-model'` 批量重新摄取受影响的块，无需全量删除重建。
6.3 问题：外网访问不稳定。
- 方案：提供本地运行模式，外网仅作为可选演示功能；Azure Container Apps 作为稳定云端备选。
6.4 问题：Prompt 效果难以量化，迭代无依据。
- 方案：Veda.Evaluation 的 Test Harness 在每次 Prompt 变更后自动运行评估，输出量化对比报告，确保每次迭代有数据支撑。
6.5 问题：Agent 编排复杂，调试困难。
- 方案：每个 Agent 有独立日志 + 状态追踪；EF Core 持久化 Agent 执行历史，便于复盘问题。
6.6 问题：MCP 工具调用引入外部依赖，测试成本高。
- 方案：MCP Tool 接口化，单元测试使用 Mock 实现；集成测试仅在 CI 中使用真实工具。
6.7 问题：`SqliteVectorStore.SearchAsync` 全量加载向量至内存，数据量大时性能下降。
- 当前设计：全量 `ToListAsync` 后在内存中做余弦相似度计算，< 10 万块性能可接受。
- 触发升级信号：单次查询延迟 > 1s 或内存占用 > 500MB。
- 阶段三方案：引入 `sqlite-vec`（`sqlite-vss` 继任者），将 ANN 检索下推到 SQLite 扩展层，只取候选集进内存。
- 阶段三/云端方案：切换 `IVectorStore` 实现为 Azure AI Search，上层代码无需任何修改（OCP 原则保障）。
6.8 问题：`DocumentIngestService.IngestAsync` 中 documentId 在 Embedding 生成前已分配，若 Embedding 调用超时或失败，调用方无法感知文档是否已入库。
- 当前行为（安全）：Embedding 失败 → 流程在写 DB 之前中断 → 文档完全未存入，documentId 不可用。重新摄取即可，幂等安全。
- Phase 4 背景任务场景的补充方案：引入 `DocumentMeta` 表（已在 EF Core 初始迁移计划中）记录摄取状态（Pending / Completed / Failed），API 先返回 documentId + Pending 状态，后台任务更新状态；客户端可轮询或通过 SSE 接收完成通知。


## 7. 项目模块结构 (Solution Structure)

```
VedaAide.NET/
├── src/
│   ├── Veda.Core/            # 契约、模型、枚举、接口
│   ├── Veda.Services/        # AI 服务：Embedding、LLM、防幻觉
│   ├── Veda.Prompts/         # Prompt 模板管理、上下文窗口构建
│   ├── Veda.Agents/          # Microsoft Agent Framework + MCP 工具
│   ├── Veda.Storage/         # VectorDbProvider (SQLite-VSS) + VedaDbContext (EF Core)
│   ├── Veda.Evaluation/      # AI Test Harness、评估指标、报告
│   ├── Veda.Api/             # ASP.NET Core Web API + GraphQL (HotChocolate)
│   └── Veda.Web/             # Angular + TypeScript 前端
├── tests/
│   ├── Veda.Core.Tests/
│   ├── Veda.Services.Tests/
│   ├── Veda.Agents.Tests/
│   └── Veda.Evaluation.Tests/
└── docs/
    └── designs/
        └── init.md
```


## 9. 编码规范与原则 (Coding Standards)

所有代码必须满足以下原则，确保可维护性、可测试性和可扩展性。


### 9.1 SOLID 原则

#### S — 单一职责原则 (SRP)
- 每个类/服务只处理一个关注点。
- 示例：`DocumentIngestService` 只负责摄取流程；`QueryService` 只负责问答查询。不允许一个 Service 类同时处理两者。
- 向量数学（`VectorMath`）独立于存储实现（`SqliteVectorStore`），两者不混用。

#### O — 开闭原则 (OCP)
- 分块策略通过 `ChunkingOptions.ForDocumentType()` 配置驱动，新增文档类型只需扩展枚举 + 配置，不修改分块器逻辑。
- `IVectorStore`、`IDocumentProcessor`、`IChatService` 均为接口，底层实现可替换（SQLite → Azure AI Search）而不改上层。

#### L — 里氏替换原则 (LSP)
- 所有接口的实现类必须完整实现合约语义，不允许抛出 `NotImplementedException` 敷衍实现。
- 替换 `SqliteVectorStore` 为 `AzureAiSearchVectorStore` 时，上层代码无需修改。

#### I — 接口隔离原则 (ISP)
- 接口按职责最小化：`IDocumentIngestor`（写）与 `IQueryService`（读）分开定义。
- Controller 只注入自己需要的接口：`DocumentsController` 只依赖 `IDocumentIngestor`，`QueryController` 只依赖 `IQueryService`。
- 避免"胖接口"：接口方法数不应超过 5 个。

#### D — 依赖倒置原则 (DIP)
- 领域层（`Veda.Core`）不依赖任何框架包。所有框架类型（SK 的 `IChatCompletionService` 等）必须在 `Veda.Core.Interfaces` 中用我们自己的接口封装后才能进入业务逻辑。
- `Veda.Services` 依赖 `Veda.Core` 接口，而不是反过来。
- 具体实现类（`SqliteVectorStore`、`OllamaChatService`）通过 DI 容器注入，业务代码不直接 `new` 实例。


### 9.2 DRY 原则 (Don't Repeat Yourself)
- 重复逻辑提取为共享方法或扩展：`DocumentTypeParser` 统一处理 `DocumentType` 枚举解析，全项目复用。
- `UpsertAsync` 不重复 `UpsertBatchAsync` 的逻辑，前者应委托后者实现。
- Prompt 模板集中在 `Veda.Prompts` 管理，不在各 Service 中散落硬编码字符串。


### 9.3 KISS 原则 (Keep It Simple)
- Phase 1 优先功能正确，不过早优化。例如向量检索先用内存余弦相似度，等数据量超过 10 万块再引入专用向量库。
- 避免不必要的抽象层：没有第二种实现需求前，不创建 Factory 或 Strategy 类。


### 9.4 YAGNI 原则 (You Aren't Gonna Need It)
- 不为"将来可能"的需求提前写代码。
- 例如：Phase 1 不实现分布式缓存、不实现多租户隔离，待真实需求出现时再扩展。


### 9.5 输入验证 (Input Validation)
- **系统边界**（API 入口）必须验证：API models 使用 `[Required]`、`[Range]`、`[MaxLength]` Data Annotations，由 ASP.NET Core 框架自动校验并返回 400。
- **内部调用**不重复验证：Service 层信任 Controller 已验证的数据，只在关键路径使用 `ArgumentException.ThrowIfNullOrWhiteSpace`。


### 9.6 测试要求
- 所有公共接口方法必须有对应单元测试（**NUnit + Moq + FluentAssertions**）。
- 测试命名遵循 `方法名_场景_ShouldAction` 格式，例如 `Process_EmptyContent_ShouldThrowArgumentException`。
- 断言统一使用 **FluentAssertions**（`x.Should().Be(y)`），禁止使用 `Assert.*`。
- AI 相关测试使用 Mock LLM，不依赖真实网络，保证 CI 稳定性。
- 测试覆盖率目标：核心业务逻辑（`Veda.Core`、`Veda.Services`）≥ 80%。


### 9.7 命名规范
- 接口以 `I` 开头：`IDocumentIngestor`、`IQueryService`。
- 结果/数据传输对象使用 C# `record`（不可变）：`IngestResult`、`RagQueryRequest`。
- **DI 扩展类统一命名为 `ServiceCollectionExtensions`**，每个项目（`Veda.Services`、`Veda.Storage` 等）各持有一个，不使用其他后缀（如 `ServicesServiceExtensions`、`StorageServiceExtensions`）。
- 扩展方法类（非 DI 注册）按功能命名：`DocumentTypeParser`、`VectorMath`。
- 私有 helper 方法用 `PascalCase`（C# 惯例）；局部变量用 `camelCase`。


### 9.8 禁止魔法数字 (No Magic Numbers/Strings)
- 代码中不允许出现无名称的字面量（数字、字符串），必须提取为具名常量或配置项。
- **类级常量**：只在单个类内使用的阈值/限制，用 `private const` 定义在该类顶部。
  ```csharp
  // ✅ 正确
  private const int SourceContentMaxLength = 200;
  content.Length > SourceContentMaxLength ? content[..SourceContentMaxLength] + "..." : content;
  
  // ❌ 错误
  content.Length > 200 ? content[..200] + "..." : content;
  ```
- **跨类共享常量**：多处复用的常量集中到 `Veda.Core` 的专用静态类（如 `RagDefaults`）。
- **可配置阈值**：用户可能需要调整的参数（如相似度阈值、TopK 默认值）放入 `appsettings.json`，通过 `IOptions<T>` 注入，不硬编码。
- HTTP 状态码通过 `StatusCodes.Status200OK` 等框架常量引用，而非直接写 `200`、`400`。


## 8. 对标 Buildxact JD 技术覆盖清单

| JD 要求 | 项目对应实现 | 模块 |
|---|---|---|
| C#, ASP.NET Core, EF Core | 全栈后端 + 数据库访问 | Veda.Api, Veda.Storage |
| Semantic Kernel | LLM 编排 + Plugin 体系 | Veda.Services, Veda.Agents |
| MCP (Model Context Protocol) | 工具调用标准化 | Veda.Agents |
| Microsoft Agent Framework | 多 Agent 编排 | Veda.Agents |
| LLM + RAG | 核心问答流程 | Veda.Services |
| Prompt/Context Engineering | 版本化模板 + Token 优化 | Veda.Prompts |
| AI Model Evaluation / Test Harness | 自动化评估体系 | Veda.Evaluation |
| Azure (Blob, OpenAI, Container Apps) | 云端存储、LLM、部署 | Veda.Storage, Veda.Api |
| Angular + TypeScript (前端) | Web UI | Veda.Web |
| GraphQL / HotChocolate | API 层 | Veda.Api |
| 自动化测试 (NUnit + FluentAssertions) | 单元 + 集成 + AI 评估测试 | tests/ |
| SaaS / Cloud-First | Azure Container Apps 部署 | 部署配置 |
 
 
