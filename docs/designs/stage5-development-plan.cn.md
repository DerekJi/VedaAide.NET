# VedaAide 五期开发计划与实施路线

> 基准文档：[三期开发计划](./stage3-development-plan.cn.md)、[四期 MCP+Agent 设计](./phase4-mcp-agents.cn.md)
>
> 形成背景：云端多用户部署安全讨论（2026-03-27），聚焦用户身份隔离、前端反馈 UX、
> MCP 通道安全分层三个核心问题。

**文档目标**：定义五期开发范围、模块设计、实施路线及验收标准，作为工程落地的参考基线。

---

## 1. 背景与目标

### 1.1 四期产出基础

VedaAide 四期已完成：

- MCP Server（HTTP/SSE），暴露 `search_knowledge_base` / `list_documents` / `ingest_document`
- Semantic Kernel Agent 编排（IRCoT 多轮推理）
- `KnowledgeScope` + `OwnerId` 多用户数据模型（存储层）
- `IKnowledgeGovernanceService`（共享组、文档授权、共识候选）
- `IUserMemoryStore` + `FeedbackBoostService`（行为事件采集、个性化 boost）
- `BehaviorType` 枚举（ResultAccepted / ResultRejected / SourceClicked / QueryRefined）

### 1.2 五期目标

四期虽然设计了用户隔离的数据模型，但**没有真实的用户身份**——`OwnerId` 是由前端自行传入的字符串，
任何人可以伪造任何 `userId`，导致：

1. **数据隔离形同虚设**：API 没有认证，scope 过滤不可信
2. **反馈学习无法个性化**：`UserId` 来源不可靠，boost 效果无意义
3. **MCP 通道无边界**：`ingest_document` 对所有连接方开放，无用户归属

五期核心目标：

| 目标 | 优先级 | 说明 |
|------|--------|------|
| **多用户身份认证** | P0 | Azure AD B2C，支持 Google/Microsoft 账号登录，省去用户注册管理 |
| **用户数据隔离** | P0 | JWT Bearer 认证，`OwnerId` 从 Token 提取，后端强制校验 |
| **前端反馈 UX** | P1 | 隐式反馈（复制、展开来源、追问）+ 轻量显式反馈（👍👎）|
| **MCP 安全分层** | P1 | 公共知识库通道（API Key，只读）vs 用户私有通道（JWT，隔离写入）|
| **示例文档库** | P2 | 预置文档 + 一键 ingest，招聘方可零配置体验问答效果 |
| **文档浏览端点** | P2 | `GET /api/documents`（文档列表）+ chunk 内容可见，辅助验证答案 |

---

## 2. 模块架构变化

在四期架构基础上，五期主要变化集中在两个层：

```
┌─────────────────────────────────────────────────────────┐
│                     Veda.Web (Angular)                  │
│  新增：LoginComponent / FeedbackBar / DocumentBrowser   │
├─────────────────────────────────────────────────────────┤
│                     Veda.Api                            │
│  新增：JWT Bearer 中间件，替代纯 ApiKey 用户端认证         │
│  新增：GET /api/documents（列表+chunk浏览）               │
│  变更：所有用户端点从 Header 取 userId → 从 Token 提取    │
├─────────────────────────────────────────────────────────┤
│                   Veda.MCP                              │
│  变更：/mcp 端点限公共知识库只读，禁用 IngestTools         │
│  新增：可选 Admin Key 鉴权的 ingest 通道                  │
├─────────────────────────────────────────────────────────┤
│  Veda.Storage / Veda.Services（无变化，已就绪）           │
└─────────────────────────────────────────────────────────┘
```

---

## 3. 功能模块详细设计

### 3.1 多用户身份认证（Azure AD B2C）

#### 3.1.1 为什么选 Azure AD B2C

| 方案 | 优点 | 缺点 |
|------|------|------|
| **Azure AD B2C** | 同时支持 Google、Microsoft、GitHub；与 Azure 基础设施天然集成；免费 50,000 MAU | 配置稍复杂（需建租户） |
| Auth0 / Clerk | 配置最简，开箱即用 | 额外平台依赖，有费用 |
| 自建用户表 | 完全控制 | 需实现注册/密码管理/邮件验证，工作量大 |
| Azure Static Web Apps 内置认证 | 零代码 | 仅适用 SWA，不适合 Container Apps |

**结论**：选 Azure AD B2C。招聘方可用 Microsoft/Google 账号直接登录，无需注册，演示体验最好。

#### 3.1.2 认证流程

```
[浏览器]
  1. 点击"登录" → 重定向到 B2C 登录页
  2. 选择 Google / Microsoft 账号 → OAuth 授权
  3. B2C 返回 JWT（id_token + access_token）
  4. Angular 存储 token（sessionStorage，非 localStorage）

[API 请求]
  5. 每次请求附带 Authorization: Bearer <access_token>
  6. JwtBearerMiddleware 验证签名 + 过期
  7. 从 token claims 提取 userId（oid 字段）
  8. 注入 ICurrentUserService.UserId，后续请求全程使用
```

#### 3.1.3 后端认证改造

**新增 `ICurrentUserService`**

```csharp
// Veda.Core.Interfaces
public interface ICurrentUserService
{
    string? UserId { get; }       // null = 未登录 / 匿名
    bool IsAuthenticated { get; }
}

// Veda.Api — 从 HttpContext.User 提取
public sealed class HttpContextCurrentUserService(IHttpContextAccessor accessor)
    : ICurrentUserService
{
    public string? UserId =>
        accessor.HttpContext?.User.FindFirst("oid")?.Value
        ?? accessor.HttpContext?.User.FindFirst("sub")?.Value;
    public bool IsAuthenticated =>
        accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
}
```

**`QueryController` 改造**（移除前端自传 `userId`）

```csharp
// 之前：userId 由前端传入，不可信
public async Task<IActionResult> Query([FromBody] QueryRequest request)
{
    var ragRequest = new RagQueryRequest { UserId = request.UserId, ... }; // ← 危险
}

// 之后：userId 从经过验证的 Token 中提取
public async Task<IActionResult> Query(
    [FromBody] QueryRequest request,
    [FromServices] ICurrentUserService currentUser)
{
    var ragRequest = new RagQueryRequest
    {
        UserId = currentUser.UserId,            // ← 来自 JWT，可信
        Scope  = currentUser.UserId is not null
            ? new KnowledgeScope(OwnerId: currentUser.UserId)
            : null,
        ...
    };
}
```

**`ApiKeyMiddleware` 保留策略**

| 路径 | 认证方式 | 说明 |
|------|----------|------|
| `/api/admin/*` | Admin API Key（不变） | 开发/运维专用 |
| `/api/*`（用户端） | JWT Bearer（新增） | 取消 X-Api-Key，改为 B2C token |
| `/mcp` | API Key（收紧，见 3.3） | MCP 通道独立鉴权 |

#### 3.1.4 Angular 认证集成

使用 `@azure/msal-angular`：

```typescript
// app.config.ts
MsalModule.forRoot(
  new PublicClientApplication({
    auth: {
      clientId: environment.b2cClientId,
      authority: 'https://<tenant>.b2clogin.com/.../B2C_1_signupsignin',
      knownAuthorities: ['<tenant>.b2clogin.com'],
    }
  }),
  { interactionType: InteractionType.Redirect },
  { interactionType: InteractionType.Redirect, protectedResourceMap }
)
```

---

### 3.2 前端反馈 UX 设计

#### 3.2.1 隐式反馈（无感知）

隐式反馈从用户的自然行为中推断，**无需用户主动操作**，前端静默调用 `POST /api/feedback`。

| 行为 | BehaviorType | 触发时机 | 技术实现 |
|------|-------------|---------|---------|
| 复制回答文本 | `ResultAccepted` | 用户在回答区 Ctrl+C / 右键复制 | `document.addEventListener('copy', ...)` |
| 展开来源引用块 | `SourceClicked` | 用户点击 `▶ 来源：文档名` 展开 | click 事件，传 `relatedChunkId` |
| 继续追问 | `ResultAccepted` | 用户发出新问题（基于上一轮） | 发送新请求时，把上一轮 chunkIds 批量上报 |
| 重新提问 | `ResultRejected` | 用户在同一 session 发出语义相似的新问题 | 可选：前端检测相似 query，打负向标记 |
| 追问细化 | `QueryRefined` | 用户的问题引用了上一个回答中的词语 | 可选：简单关键词重叠检测 |

**关键设计原则**：用户感知不到任何"打分"动作，但系统持续学习。

#### 3.2.2 显式反馈（轻量）

在回答区底部添加一个极简反馈条，**只在整个回答末尾出现一次**：

```
┌─────────────────────────────────────────────┐
│  这个回答有帮助吗？  [👍 有用]  [👎 没帮助]   │
└─────────────────────────────────────────────┘
```

- 点击后按钮变为已选中状态，**不弹出弹窗**，不打断对话流
- 映射：👍 → `ResultAccepted`，👎 → `ResultRejected`
- 已点击后按钮置灰（防止重复提交）

#### 3.2.3 来源展示与关联

回答区 `Sources` 展示改造，让用户能感知"答案从哪里来"：

```
┌──────────────────────────────────────────────────────┐
│  AI 回答正文...                                        │
│                                                      │
│  📎 参考来源（3 处）                                   │
│  ▶ Q4-2025-财务报告.pdf  (相似度 94%)                  │
│     [展开] → 显示 ChunkContent 原文片段                │
│  ▶ 系统架构说明.md  (相似度 88%)                       │
│  ▶ 合同条款-2025.pdf  (相似度 79%)                     │
│                                                      │
│  这个回答有帮助吗？  [👍 有用]  [👎 没帮助]             │
└──────────────────────────────────────────────────────┘
```

展开操作触发 `SourceClicked` 事件，传入对应的 `chunkId`。

#### 3.2.4 让用户感知到"系统在进化"

`FeedbackBoostService` 已实现，但用户感知不到。可以加一个细节：

- 在"参考来源"旁显示 `★ 常用` 标签（当某个来源的 boost > 1.5 时）
- 这个标签不需要后端新字段，前端用 boost 阈值判断即可（需后端在 Source 里返回 boost 值，或用 similarity 作为代理指标）

---

### 3.3 MCP 安全分层

#### 3.3.1 问题根源

当前 MCP 工具的三个安全问题：

1. `/mcp` 端点在 `ApiKeyMiddleware.IsExcluded` 列表中被跳过（实际未做任何认证）
2. `KnowledgeBaseTools.SearchKnowledgeBase` 搜索全局数据，无 scope 过滤
3. `IngestTools.IngestDocument` 写入的数据无 `OwnerId`，成为"野数据"

#### 3.3.2 通道分层方案

将知识库分为两层，MCP 只服务**公共层**：

```
                    ┌─────────────────────────────┐
外部 AI 客户端  →   │  /mcp  (公共知识库，只读)    │
(Copilot Chat)      │  认证：Admin API Key 或 开放  │
                    │  数据：Visibility=Public      │
                    └─────────────────────────────┘

                    ┌─────────────────────────────┐
用户浏览器      →   │  /api/*  (用户私有知识库)    │
                    │  认证：JWT Bearer (B2C)       │
                    │  数据：OwnerId = userId       │
                    └─────────────────────────────┘
```

#### 3.3.3 具体改造

**`KnowledgeBaseTools` 改造：固定 scope 为 Public**

```csharp
// 搜索时强制限定 Visibility=Public，不暴露任何用户私有数据
var results = await vectorStore.SearchAsync(
    queryEmbedding, topK,
    scope: new KnowledgeScope(Visibility: Visibility.Public),
    ct: cancellationToken);
```

**禁用 `IngestTools`（或限 Admin Key）**

选项 A — 完全移除 `IngestTools` 注册：
```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<KnowledgeBaseTools>();  // 移除 .WithTools<IngestTools>()
```

选项 B — 保留但加 Admin Key 鉴权（仅预置演示文档时使用）。

**`/mcp` 端点从 Excluded 列表移出，加 API Key 认证**

```csharp
// ApiKeyMiddleware 改造：/mcp 使用 AdminApiKey 鉴权
if (path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase))
{
    var adminKey = cfg["Veda:Security:AdminApiKey"];
    if (!string.IsNullOrWhiteSpace(adminKey) && requestKey != adminKey)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "MCP requires Admin API key." });
        return;
    }
}
```

---

### 3.4 示例文档库（Demo Knowledge Base）

为招聘方提供"零配置体验"路径。

#### 3.4.1 设计

Azure Blob Storage 中维护一个 `demo-documents/` 前缀，存放预置文档（如技术架构说明、FAQ、简历样本）。

**新增 API 端点**

```
GET  /api/demo/documents          → 列出可用的预置文档（名称、描述、大小）
POST /api/demo/documents/{name}/ingest  → 将指定预置文档 ingest 到当前用户的知识库
```

后端实现：调用 `BlobStorageConnector` 读取指定 Blob，
`IngestAsync` 时传入 `KnowledgeScope(OwnerId: currentUser.UserId, Visibility: Visibility.Private)`。

**前端："示例文档库"面板**

```
┌─────────────────────────────────────────────┐
│  📚 示例文档库                               │
│  勾选后点击"加载到知识库"即可开始提问          │
│                                             │
│  ☐ VedaAide 系统架构说明 (24KB, Markdown)   │
│  ☐ 技术问答 FAQ (12KB, Markdown)            │
│  ☐ 项目财务报告样本 (156KB, PDF)             │
│                                             │
│            [加载到我的知识库]                 │
│                                             │
│  💡 推荐问题：                               │
│  • "VedaAide 使用了哪些 AI 技术？"           │
│  • "项目的架构是怎么设计的？"                 │
└─────────────────────────────────────────────┘
```

---

### 3.5 文档浏览与管理端点

#### 3.5.1 文档列表与 Chunk 浏览

**新增 `GET /api/documents`**

```csharp
// DocumentsController 新增
[HttpGet]
public async Task<IActionResult> ListDocuments(
    [FromServices] ICurrentUserService currentUser,
    CancellationToken ct)
{
    var scope = currentUser.UserId is not null
        ? new KnowledgeScope(OwnerId: currentUser.UserId)
        : null;
    var docs = await vectorStore.GetAllDocumentsAsync(ct);
    // 过滤：仅返回当前用户的文档
    return Ok(docs);
}

// GET /api/documents/{documentName}/chunks  → 返回所有 chunk 的文本内容
[HttpGet("{documentName}/chunks")]
public async Task<IActionResult> GetChunks(string documentName, CancellationToken ct)
{
    var chunks = await vectorStore.GetCurrentChunksByDocumentNameAsync(documentName, ct);
    return Ok(chunks.Select(c => new {
        c.ChunkIndex,
        c.Content,
        c.DocumentType
    }));
}
```

#### 3.5.2 数据清除（前端可操作）

后端 `AdminController` 已提供三个删除端点：

| 端点 | 功能 |
|------|------|
| `DELETE /api/admin/data`（需 `X-Confirm: yes` 头） | 清空所有 vector chunks 和同步记录 |
| `DELETE /api/admin/documents/{documentId}` | 删除指定文档的所有 chunks |
| `DELETE /api/admin/cache` | 清空语义缓存 |

前端目前缺少对应 UI，需在 **Documents 页面**增加数据管理操作：

```
┌─────────────────────────────────────────────────────┐
│  已 Ingest 的文档                          [全部清空] │
│                                                     │
│  Q4-2025-财务报告.pdf    3 chunks   [删除]           │
│  系统架构说明.md         12 chunks  [删除]           │
│  合同条款-2025.pdf       8 chunks   [删除]           │
│                                                     │
│  💡 「全部清空」将删除所有 chunks 和同步记录          │
└─────────────────────────────────────────────────────┘
```

**交互规范**：
- 「删除」单个文档：调用 `DELETE /api/admin/documents/{documentId}`，即时刷新列表
- 「全部清空」：弹出确认对话框（"此操作不可恢复，确认清空全部数据？"），确认后调用
  `DELETE /api/admin/data`（附带 `X-Confirm: yes` 请求头），成功后列表归零
- 操作中按钮禁用，操作完成后显示 Toast 提示

**权限策略**：五期引入 JWT 认证后，`/api/admin/*` 端点需限定为管理员角色
（通过 B2C 自定义 claims 或独立 AdminApiKey 双轨鉴权）。

---

## 4. 数据安全与隐私声明

### 4.1 对"开发者能看到用户数据吗"的正式回应

| 存储内容 | 可见性 | 备注 |
|---------|--------|------|
| `content`（chunk 原文） | 开发者技术上可访问 | 与行业标准一致；RAG 系统必须存原文用于引用回源 |
| `embedding`（向量） | 一堆浮点数，无原文价值 | 无法反推原文 |
| `userId`（OwnerId） | 存储为身份提供商的 `oid`，不含真实姓名 | B2C oid 是不透明 UUID |
| `UserBehaviors`（行为事件） | 仅含 chunkId + 行为类型，不含原文 | 代码注释已声明"隐私设计" |

**可选加固**：对生产 CosmosDB 启用 Customer-Managed Keys（CMK），
则即使是 Azure 员工和数据库管理员也无法读取明文，隐私保护达到行业最高标准。

### 4.2 数据隔离保证

- 用户 A 的 query 只返回 `OwnerId=A` 或 `Visibility=Public` 的 chunks
- 用户 A 无法 ingest 到用户 B 的 scope
- MCP 通道只能访问 `Visibility=Public` 数据
- `OwnerId` 由 JWT 提供，后端提取，前端无法伪造

---

## 5. 实施计划

### Sprint 1：认证基础（约 1 周）

| 任务 | 模块 | 关键产出 |
|------|------|---------|
| 创建 Azure AD B2C 租户，配置 Google/Microsoft 社交登录 | 基础设施 | B2C 租户 + 用户流 |
| `Veda.Api` 添加 JWT Bearer 中间件 | `Veda.Api` | Program.cs auth 配置 |
| 实现 `ICurrentUserService` + DI 注册 | `Veda.Api` | 可从 Token 提取 userId |
| `QueryController` / `FeedbackController` 改造，移除前端自传 userId | `Veda.Api` | 用户端 userId 来源可信 |
| Angular `@azure/msal-angular` 集成，登录/登出组件 | `Veda.Web` | 可正常 OAuth 登录 |

**验收**：用 Google 账号登录后提问，后端日志中 userId 来自 JWT，与手传值不同。

### Sprint 2：MCP 安全加固（约 3 天）

| 任务 | 模块 | 关键产出 |
|------|------|---------|
| `/mcp` 从 ApiKeyMiddleware Excluded 列表移出，加 Admin Key 鉴权 | `Veda.Api` | 未授权访问 401 |
| `KnowledgeBaseTools.SearchKnowledgeBase` 加 `Visibility=Public` scope | `Veda.MCP` | 无法检索私有数据 |
| 移除 `IngestTools` 注册（或限 Admin Key） | `Veda.MCP` | MCP 通道只读 |

**验收**：无 Admin Key 访问 `/mcp` 返回 401；用 MCP 搜索无法返回私有文档。

### Sprint 3：反馈 UX（约 1 周）

| 任务 | 模块 | 关键产出 |
|------|------|---------|
| 来源引用块 UI（文档名 + chunk 原文折叠展开） | `Veda.Web` | `SourcesPanel` 组件 |
| 展开来源 → `SourceClicked` 隐式反馈 | `Veda.Web` | 静默上报 |
| 复制回答 → `ResultAccepted` 隐式反馈 | `Veda.Web` | copy 事件监听 |
| 追问 → 上一轮 `ResultAccepted` 批量上报 | `Veda.Web` | session 内 chunkId 跟踪 |
| 显式 👍👎 反馈条（回答末尾，单次可点） | `Veda.Web` | `FeedbackBar` 组件 |

**验收**：展开来源后数据库存在 SourceClicked 事件；复制文本后存在 ResultAccepted 事件。

### Sprint 4：示例文档库 + 文档浏览（约 1 周）

| 任务 | 模块 | 关键产出 |
|------|------|---------|
| `GET /api/documents`、`GET /api/documents/{name}/chunks` | `Veda.Api` | 文档列表 + chunk 内容端点 |
| Blob Storage `demo-documents/` 前缀预置 2-3 份文档 | 基础设施 | 示例文档就位 |
| `GET /api/demo/documents`、`POST /api/demo/documents/{name}/ingest` | `Veda.Api` | 一键 ingest 端点 |
| Angular 示例文档库面板 + 推荐问题列表 | `Veda.Web` | `DemoLibraryPanel` 组件 |
| Angular 文档列表页（显示已 ingest 的文档，可点击查看 chunks） | `Veda.Web` | `DocumentBrowserComponent` |
| Angular 文档管理操作：单个文档「删除」按钮 + 「全部清空」按钮（含确认对话框） | `Veda.Web` | Documents 页数据管理 UI |
| `AdminService`（Angular）封装 `DELETE /api/admin/data` 和 `DELETE /api/admin/documents/{id}` | `Veda.Web` | 可复用的管理操作服务 |

**验收**：招聘方登录 → 勾选示例文档 → 加载 → 提问 → 答案含来源引用，全程无需上传任何文件。

---

## 6. 技术决策记录（ADR）

### ADR-501：选择 Azure AD B2C 而非自建用户系统

**决策**：使用 Azure AD B2C 的社交登录（Google + Microsoft）。

**原因**：
- 省去注册、密码重置、邮件验证等用户管理代码（估计节省 2-3 周工作量）
- 招聘方用已有的 Microsoft/Google 账号直接登录，零注册摩擦，演示体验最友好
- 返回标准 JWT，`oid` claim 可直接作为 OwnerId，无需额外映射
- 免费层 50,000 MAU，项目规模永远不会超出

### ADR-502：MCP 通道定位为"公共知识库只读接口"

**决策**：MCP `/mcp` 端点只暴露 `Visibility=Public` 的数据，禁用写入工具。

**原因**：
- MCP 的典型调用方是 Copilot Chat、Claude Desktop 等第三方 AI 客户端，
  这些客户端没有"当前登录用户"的概念，强行关联用户身份反而会出错
- "公共知识库"语义清晰：开发者用 Admin Key 预置示범文档，
  任何 MCP 客户端都可以自由搜索，不涉及隐私
- 用户私有数据全部走 `/api/*` JWT 通道，两条通道职责清晰、互不干扰

### ADR-503：隐式反馈优先于显式反馈

**决策**：优先实现复制、展开来源、追问三种隐式信号，显式 👍👎 作为补充。

**原因**：
- 隐式反馈覆盖率远高于显式（实验数据：显式点击率通常 < 5%，隐式行为覆盖率 > 60%）
- 用户不会因为"被要求评分"而产生疲劳感
- `BehaviorType.SourceClicked` / `ResultAccepted` / `QueryRefined` 已在后端定义，
  前端只需接入，无需改 API

---

## 7. 非功能性要求

| 维度 | 要求 |
|------|------|
| **安全** | JWT 签名验证（RS256），token 不存 localStorage（防 XSS），HTTPS only |
| **隐私** | OwnerId 使用 B2C oid（不透明 UUID），不存储用户真实姓名或邮箱 |
| **可观测性** | 所有反馈事件写日志（userId 脱敏），认证失败记录 401 日志 |
| **降级** | 未登录用户仍可使用（scope=null，访问 Public 数据），不强制登录 |
| **测试** | Sprint 验收条件均有对应集成测试或 E2E 场景覆盖 |
