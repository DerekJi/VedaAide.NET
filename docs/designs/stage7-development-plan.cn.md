# VedaAide 七期开发计划与实施路线

> 基准文档：[六期开发计划](./stage6-development-plan.cn.md)
>
> 形成背景：2026-04-01，聚焦会话持久化、多会话管理及邮件格式支持的工程讨论。

**文档目标**：定义七期开发范围、模块设计、实施路线及验收标准，作为工程落地的参考基线。

---

## 1. 背景与目标

### 1.1 六期产出基础

VedaAide 六期已完成：

- Azure DI 配额感知 + Vision 模型自动降级
- PDF 文字层直通提取（`PdfTextLayerExtractor`，阈值 20 字符/页）
- 证件类专用模型 `prebuilt-idDocument`
- Vision 模型 HTTP 超时可配置（`Veda:Vision:TimeoutSeconds`）
- 本地 Ollama Vision 提供商支持（`qwen2.5vl:3b` 等）
- Token 消耗统计（`TokenUsageRepository`，本月 / 全量聚合）
- `IChatCompletionService` 元数据路径兼容 `UsageDetails`（Ollama M.E.AI 路径修复）
- 管理员角色权限隔离（Azure Entra ID App Roles）
- 邮件文件摄取：EML（MimeKit）和 MSG（MsgReader）文件解析

**六期遗留问题（七期修复/完善）：**

1. Chat 页面切换路由后消息列表清空，用户体验差
2. 无多会话管理——不能并行维持多个话题上下文
3. 聊天记录无用户隔离：当前代码无任何持久化，七期存储时必须绑定到登录用户

### 1.2 七期目标

| 目标 | 优先级 | 说明 |
|------|--------|------|
| **多会话 UI：新建 / 切换 / 删除** | P0 | 侧边栏列表，支持无限新开会话，最多保留 50 条 |
| **会话跨路由持久化（内存）** | P0 | Angular 单例 Service 维持状态，切到其他页面再回来不丢失 |
| **会话本地持久化（localStorage）** | P1 | 页面刷新后从 localStorage 恢复，无需后端 |
| **会话云端持久化（后端）** | P1 | 会话及消息存入数据库，多设备同步；必须绑定 `userId`，不同用户数据严格隔离 |
| **会话标题自动生成** | P2 | 取第一条用户提问前 30 字作为标题 |
| **流式响应期间切换会话** | P2 | 自动中止当前 SSE 流，不影响目标会话 |
| **邮件 MSG 单元测试补全** | P2 | 当前 MSG 解析逻辑无测试，补充基于真实二进制 fixture 的集成测试 |

---

## 2. 架构设计

### 2.1 整体分层

```
┌─────────────────────────────────────────────────────────┐
│                   Veda.Web（Angular）                   │
│  新增：ChatSessionService（单例，跨路由状态）            │
│  新增：ChatSession 接口、ChatHistoryService（云端同步）  │
│  变更：ChatComponent — 使用 ChatSessionService 渲染      │
│  新增：侧边栏 ChatSidebarComponent                       │
└──────────────────────────┬──────────────────────────────┘
                           │ HTTP
┌──────────────────────────▼──────────────────────────────┐
│                   Veda.Api                              │
│  新增：ChatSessionController                            │
│    POST   /api/chat/sessions         创建会话            │
│    GET    /api/chat/sessions         列出当前用户所有会话 │
│    DELETE /api/chat/sessions/{id}    删除会话            │
│    GET    /api/chat/sessions/{id}/messages  获取消息列表 │
│    POST   /api/chat/sessions/{id}/messages  追加消息     │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                   Veda.Core                             │
│  新增：IChatSessionRepository                           │
│  新增：ChatSessionRecord、ChatMessageRecord（值对象）   │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│                   Veda.Storage                          │
│  新增：ChatSessionEntity、ChatMessageEntity（EF 实体）  │
│  新增：ChatSessionRepository（SQLite + CosmosDB 双实现）│
│  变更：VedaDbContext — 添加 ChatSessions、ChatMessages  │
│  变更：CosmosDbInitializer — 新增 ChatSessions 容器     │
└─────────────────────────────────────────────────────────┘
```

### 2.2 数据模型

#### 2.2.1 ChatSessionRecord（领域层）

```csharp
public record ChatSessionRecord(
    string SessionId,
    string UserId,          // 绑定登录用户，强制隔离
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
```

#### 2.2.2 ChatMessageRecord（领域层）

```csharp
public record ChatMessageRecord(
    string MessageId,
    string SessionId,
    string UserId,          // 冗余字段，便于 CosmosDB /userId 分区快速查询
    string Role,            // "user" | "assistant"
    string Content,
    float? Confidence,
    bool IsHallucination,
    IReadOnlyList<SourceRef> Sources,
    DateTimeOffset CreatedAt
);
```

#### 2.2.3 存储隔离策略

| 存储 | 隔离方式 |
|------|---------|
| SQLite | `WHERE UserId = @userId`，控制器从 JWT 读取 `UserId`，不接受客户端传入 |
| CosmosDB | Partition Key = `/userId`，天然隔离；查询时总是附带 `PartitionKey` 约束 |

**安全原则：** 所有 `ChatSessionController` 端点从 `ICurrentUserService.UserId` 取身份，不信任请求体/查询参数中的 `userId`，防止越权访问。

---

## 3. 阶段实施路线

### Phase 1：前端内存 + localStorage 持久化（无后端）

**目标**：解决切换路由后消息丢失、支持多会话创建/切换/删除，页面刷新后恢复。

#### 3.1.1 ChatSessionService（Angular 单例）

```typescript
interface ChatSession {
  id: string;
  title: string;
  createdAt: number;
  messages: ChatMessage[];
}

@Injectable({ providedIn: 'root' })
export class ChatSessionService {
  // 单例，由 Angular DI 管理生命周期
  private readonly _sessions = signal<ChatSession[]>(this.load());  // 从 localStorage 恢复
  private readonly _activeId = signal<string>('');
  private readonly _messages = signal<ChatMessage[]>([]);

  newSession(): string;
  switchSession(id: string): void;
  deleteSession(id: string): void;
  addMessage(msg: ChatMessage): void;
  refreshMessages(): void;    // 流式 token 追加期间触发变更检测（不写 localStorage）
  finalizeMessage(): void;    // 流结束后同步 + 持久化
  setTitle(id: string, title: string): void;
}
```

要点：
- `newSession()` 无参数，自动生成 UUID 和默认标题 "New Chat"
- `messages` 独立维护（不每次从 `sessions` 数组提取），减少深拷贝开销
- 流式写入期间（高频 token 事件）只调用 `refreshMessages()`，不写 localStorage，流完成后一次性 `finalizeMessage()`
- 持久化时过滤 `streaming: true` 的消息，防止刷新后出现未完成的气泡

#### 3.1.2 ChatComponent 变化

- 注入 `ChatSessionService`，移除本地 `messages` signal
- 流结束后调用 `setTitle()`（`messages.length === 1` 时取第一条用户提问前 30 字）
- 流中断时（切换会话、关闭页面）调用 `abortController.abort()`

#### 3.1.3 侧边栏 UI

Chat 页改为两列布局：

```
┌── 侧边栏 220px ──┬──── 聊天主区域（flex: 1）────┐
│ [+ New Chat ]   │                              │
│ ─────────────   │  消息气泡区域                 │
│ ▶ 今天          │                              │
│   学校成绩查询  │                              │
│   钢琴水平问题  │  [输入框]    [Ask]            │
│ ▶ 昨天          │                              │
│   ICAS 成绩     │                              │
└─────────────────┴──────────────────────────────┘
```

- 会话列表按日期分组（今天 / 昨天 / 更早）
- 鼠标悬停显示删除按钮（���）
- 当前激活会话高亮
- 侧边栏可折叠（按钮切换，状态存 localStorage）

#### 3.1.4 验收标准

- [ ] 切换到 Documents 页再回 Chat，消息列表完整保留
- [ ] 新建会话后输入框清空，旧会话内容不受影响
- [ ] 删除会话后自动切换到最新会话（无会话时自动新建）
- [ ] 刷新页面后最新会话内容从 localStorage 恢复
- [ ] 流式输出期间切换会话，旧流自动中止，不影响新会话

---

### Phase 2：后端会话持久化 + 用户隔离

**目标**：会话数据存入数据库，不同用户严格隔离，支持多设备同步。

#### 3.2.1 新增端点

```
POST   /api/chat/sessions
  Body: { title?: string }
  → 创建新会话，UserId 从 JWT 注入，返回 { sessionId, title, createdAt }

GET    /api/chat/sessions
  → 返回当前用户所有会话列表（按 updatedAt 降序）

DELETE /api/chat/sessions/{id}
  → 删除会话（验证 session.UserId == currentUser.UserId，否则 403）

GET    /api/chat/sessions/{id}/messages
  → 返回会话消息列表（验证归属）

POST   /api/chat/sessions/{id}/messages
  Body: { role, content, confidence?, isHallucination?, sources? }
  → 追加消息（仅 assistant 消息由后端写入，user 消息由前端触发）
```

#### 3.2.2 ChatSessionRepository 接口

```csharp
public interface IChatSessionRepository
{
    Task<ChatSessionRecord> CreateAsync(string userId, string title, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSessionRecord>> ListAsync(string userId, CancellationToken ct = default);
    Task DeleteAsync(string sessionId, string userId, CancellationToken ct = default);

    Task AppendMessageAsync(ChatMessageRecord message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessageRecord>> GetMessagesAsync(string sessionId, string userId, CancellationToken ct = default);
}
```

注：`userId` 始终作为参数传入，Repository 级别也强制校验，形成双重防护。

#### 3.2.3 SQLite 实体与迁移

```csharp
public class ChatSessionEntity
{
    public string SessionId    { get; set; } = Guid.NewGuid().ToString();
    public string UserId       { get; set; } = string.Empty;
    public string Title        { get; set; } = "New Chat";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ChatMessageEntity> Messages { get; set; } = [];
}

public class ChatMessageEntity
{
    public string MessageId    { get; set; } = Guid.NewGuid().ToString();
    public string SessionId    { get; set; } = string.Empty;
    public string UserId       { get; set; } = string.Empty;  // 冗余，便于隔离查询
    public string Role         { get; set; } = string.Empty;
    public string Content      { get; set; } = string.Empty;
    public float? Confidence   { get; set; }
    public bool IsHallucination { get; set; }
    public string SourcesJson  { get; set; } = "[]";  // JSON 序列化
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

#### 3.2.4 CosmosDB 实现

- 容器：`ChatSessions`，Partition Key = `/userId`
- 每条消息内嵌在 document 中（MongoDB 风格），或拆分为独立文档（视消息量决定）
- 建议：会话 document 内嵌前 20 条消息，超出后独立存储

#### 3.2.5 前端改造

`ChatSessionService` 增加后端同步层：

```
本地操作（立即响应 UI）→ 异步同步到后端 → 失败时本地 localStorage 兜底
```

- 初始化时：优先从后端拉取会话列表（有网络时），失败时 fallback 到 localStorage
- 新建/删除会话：本地先更新，后端异步写入
- 消息写入：用户消息本地存，助手消息在流结束后 POST 到后端

#### 3.2.6 验收标准

- [ ] 用户 A 无法读取/删除用户 B 的会话（403）
- [ ] 换设备登录后能看到历史会话
- [ ] 后端不可用时（网络断开）本地 localStorage 正常工作
- [ ] 删除账号时（未来功能）级联删除所有会话和消息

---

### Phase 3：体验优化

**目标**：在 P0/P1 功能稳定后提升整体 UX 和可维护性。

| 功能 | 说明 |
|------|------|
| 会话标题手动编辑 | 双击会话标题进入编辑模式，回车保存 |
| 会话搜索 | 侧边栏顶部搜索框，按标题过滤 |
| 消息复制按钮 | 助手气泡悬停显示 ���，一键复制内容 |
| 代码块渲染 | 用 `marked` 或 `highlight.js` 渲染 Markdown 代码块 |
| MSG 测试补全 | 用真实 `.msg` fixture 补充 MsgReader 集成测试 |
| 会话导出 | 导出为 Markdown 或 JSON 文件 |

---

## 4. 文件变更清单

### 4.1 新增文件

| 文件 | 说明 |
|------|------|
| `Veda.Web/.../services/chat-session.service.ts` | Angular 单例，跨路由状态 + localStorage 持久化 |
| `Veda.Web/.../components/chat-sidebar/chat-sidebar.component.ts` | 会话侧边栏组件 |
| `Veda.Web/.../components/chat-sidebar/chat-sidebar.component.html` | 侧边栏模板 |
| `Veda.Web/.../components/chat-sidebar/chat-sidebar.component.scss` | 侧边栏样式 |
| `Veda.Core/Interfaces/IChatSessionRepository.cs` | 仓储接口 |
| `Veda.Core/Interfaces/ChatSessionRecord.cs` | 领域值对象 |
| `Veda.Storage/Entities/ChatSessionEntity.cs` | EF 实体 |
| `Veda.Storage/Entities/ChatMessageEntity.cs` | EF 实体 |
| `Veda.Storage/ChatSessionRepository.cs` | SQLite 实现 |
| `Veda.Storage/CosmosDbChatSessionRepository.cs` | CosmosDB 实现 |
| `Veda.Api/Controllers/ChatSessionController.cs` | REST 端点 |
| `tests/.../ChatSessionRepositoryTests.cs` | 仓储单元测试 |
| `tests/.../ChatSessionControllerTests.cs` | 控制器集成测试 |

### 4.2 修改文件

| 文件 | 变更说明 |
|------|---------|
| `Veda.Web/.../chat/chat.component.ts` | 注入 `ChatSessionService`，移除本地状态 |
| `Veda.Web/.../chat/chat.component.html` | 添加侧边栏，引用新组件 |
| `Veda.Web/.../chat/chat.component.scss` | 双列布局，侧边栏样式 |
| `Veda.Storage/VedaDbContext.cs` | 添加 `ChatSessions`、`ChatMessages` DbSet |
| `Veda.Storage/ServiceCollectionExtensions.cs` | 注册 `ChatSessionRepository` |
| `Veda.Storage/CosmosDbInitializer.cs` | 初始化 `ChatSessions` 容器 |
| `Veda.Api/Program.cs` | 注册 `ChatSessionController` 依赖 |
| `tests/GlobalUsings.cs` | 添加新测试命名空间 |

---

## 5. 安全设计要点

| 风险 | 防护 |
|------|------|
| 用户越权访问他人会话 | 控制器从 `ICurrentUserService.UserId` 取身份；Repository 所有查询携带 `userId` 约束；返回 403（不返回 404，避免信息泄露） |
| 匿名用户访问会话 API | `[Authorize]` 装饰器，无 Bearer Token 时返回 401 |
| XSS（消息内容渲染） | Phase 3 引入 Markdown 渲染时使用 `DOMPurify` 净化 HTML |
| localStorage 敏感数据 | 本地仅存消息文本，不存 Token 或凭据；敏感业务场景建议加密（Phase 3 可选）|
| 消息内容过大 | 单条消息内容限制 10,000 字符，超出时截断并记录警告 |

---

## 6. 测试策略

### 6.1 单元测试

| 测试用例 | 覆盖场景 |
|---------|---------|
| `ChatSessionService_NewSession_ShouldCreateAndActivate` | 新建会话后 activeId 更新，sessions 包含新会话 |
| `ChatSessionService_SwitchSession_ShouldLoadMessages` | 切换后 messages 替换为目标会话内容 |
| `ChatSessionService_DeleteActiveSession_ShouldSwitchToNext` | 删除当前会话后切换到下一个，无会话时自动新建 |
| `ChatSessionService_Persistence_ShouldSurviveReload` | 写入 localStorage 后重新初始化，数据恢复 |
| `ChatSessionRepository_ListAsync_ShouldOnlyReturnOwnSessions` | 用户只能看到自己的会话 |
| `ChatSessionRepository_DeleteAsync_WrongUser_ShouldThrow` | 删除他人会话时抛出 UnauthorizedAccessException |

### 6.2 集成测试

| 测试用例 | 验证 |
|---------|------|
| POST /api/chat/sessions 创建成功 | 返回 201，body 包含 sessionId |
| GET /api/chat/sessions 只返回本人会话 | 多用户场景隔离验证 |
| DELETE /api/chat/sessions/{otherId} | 返回 403 |
| GET /api/chat/sessions/{id}/messages 未授权 | 返回 401 |

---

## 7. 时间估算

| Phase | 预估工作量 | 优先级 |
|-------|-----------|--------|
| Phase 1：前端内存 + localStorage | 2–3 天 | P0 |
| Phase 2：后端持久化 + 用户隔离 | 3–4 天 | P1 |
| Phase 3：体验优化 | 2–3 天 | P2 |
| **合计** | **7–10 天** | — |

---

## 8. 验收定义（DoD）

- [ ] 编译通过，无 0 Warning 目标（`<TreatWarningsAsErrors>` 检查）
- [ ] 所有新增单元测试绿灯
- [ ] 手动验证：用户 A 登录后无法看到用户 B 的历史会话
- [ ] 手动验证：切换路由再回 Chat，当前会话消息完整
- [ ] 手动验证：刷新页面后最近会话恢复
- [ ] 手动验证：流式输出中途切换会话，旧流中止，不出现消息串台

---

## 附录 A：Vision 提供商独立配置（P1）

### A.1 背景与动机

当前架构中，Vision 提供商与 Chat LLM 提供商**强耦合**：

- `LlmProvider = AzureOpenAI` → Vision 自动复用 gpt-4o-mini
- `LlmProvider = Ollama`      → Vision 只能用本地 Ollama VL 模型

这导致一个常见场景无法满足：**Chat 走本地 Ollama（低延迟、免费），但 Vision 走 Azure OpenAI（高精度 OCR）**。本附录定义解耦方案。

### A.2 配置设计

在 `Veda:Vision` 节增加两个字段：

```json
"Vision": {
  "Enabled": true,
  "OllamaModel": "",               // Ollama VL 模型名（如 qwen3-vl:8b）；非空则优先使用
  "ChatDeployment": "gpt-4o-mini", // AzureOpenAI 视觉部署名
  "TimeoutSeconds": 300
}
```

**优先级规则（无需显式 ModelProvider，由数据决定）：**

| `Vision:OllamaModel` | `AzureOpenAI:Endpoint` | 实际 Vision 服务 |
|---|---|---|
| 非空 | 任意 | Ollama VL 模型（独立 HttpClient，含超时配置） |
| 空 | 非空 | AzureOpenAI `ChatDeployment`（与主 LlmProvider 无关） |
| 空 | 空 | fallback：复用主 Chat 服务 |

典型场景：dev 环境 `LlmProvider=Ollama`，`OllamaModel` 留空，`AzureOpenAI:Endpoint` 填入 → Vision 自动走 AzureOpenAI，无需任何额外 Provider 标记。

### A.3 代码变更

#### A.3.1 VisionOptions 变更

```csharp
public sealed class VisionOptions
{
    public bool    Enabled        { get; set; } = false;
    public string? OllamaModel    { get; set; }            // Ollama VL 模型名（非空则使用 Ollama）
    public string  ChatDeployment { get; set; } = "gpt-4o-mini";  // AzureOpenAI 部署名
    public int     TimeoutSeconds { get; set; } = 300;
}
```

#### A.3.2 ServiceCollectionExtensions 路由逻辑

```
ollamaModel = cfg["Veda:Vision:OllamaModel"]

if ollamaModel 非空:
    → 独立 HttpClient（TimeoutSeconds）
    → AddOllamaChatCompletion(ollamaModel, httpClient)
    → AddKeyedSingleton<IChatCompletionService>("vision", ...)

elif cfg["Veda:AzureOpenAI:Endpoint"] 非空:
    deployment = cfg["Veda:Vision:ChatDeployment"] ?? "gpt-4o-mini"
    → 构建 AzureOpenAIClient → AddAzureOpenAIChatCompletion(deployment)
    → AddKeyedSingleton<IChatCompletionService>("vision", ...)

else:
    → fallback 到主 Chat 服务（AddKeyedTransient）
```

完整伪代码（实现时替换 ServiceCollectionExtensions 中 Vision 注册段）：

```csharp
// ── Vision ──────────────────────────────────────────────────────────────────
var visionOpts   = cfg.GetSection("Veda:Vision").Get<VisionOptions>() ?? new();
var visionProvider = visionOpts.ModelProvider
    ?? (llmProvider.Equals("AzureOpenAI", ...) ? "AzureOpenAI" : "Ollama");

if (visionProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
{
    var endpoint   = cfg["Veda:AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("...");
    var apiKey     = cfg["Veda:AzureOpenAI:ApiKey"];
    var deployment = visionOpts.ChatDeployment;  // default "gpt-4o-mini"

    var visionAzureClient = string.IsNullOrWhiteSpace(apiKey)
        ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
        : new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

    var visionKernel = Kernel.CreateBuilder()
        .AddAzureOpenAIChatCompletion(deployment, visionAzureClient)
        .Build();
    services.AddKeyedSingleton<IChatCompletionService>("vision",
        visionKernel.GetRequiredService<IChatCompletionService>());
}
else  // Ollama
{
    var ollamaEndpoint = cfg["Veda:OllamaEndpoint"] ?? "http://localhost:11434";
    var visionModel    = visionOpts.Model;

    if (!string.IsNullOrWhiteSpace(visionModel))
    {
        var visionHttpClient = new HttpClient
        {
            BaseAddress = new Uri(ollamaEndpoint.TrimEnd('/') + "/"),
            Timeout     = TimeSpan.FromSeconds(visionOpts.TimeoutSeconds)
        };
        var visionKernel = Kernel.CreateBuilder()
            .AddOllamaChatCompletion(visionModel, visionHttpClient)
            .Build();
        services.AddKeyedSingleton<IChatCompletionService>("vision",
            visionKernel.GetRequiredService<IChatCompletionService>());
    }
    else
    {
        // 未指定 VL 模型，复用主 Chat 服务（可能不支持视觉，解析失败时有 fallback）
        services.AddKeyedTransient<IChatCompletionService>("vision",
            (sp, _) => sp.GetRequiredService<IChatCompletionService>());
    }
}
```

### A.4 典型配置示例

#### 场景 1：Chat 本地 Ollama + Vision Azure OpenAI（推荐本地开发）

```json
"LlmProvider": "Ollama",
"ChatModel": "qwen3:8b",
"AzureOpenAI": {
  "Endpoint": "https://my-resource.openai.azure.com/",
  "ApiKey": "sk-..."
},
"Vision": {
  "Enabled": true,
  "ModelProvider": "AzureOpenAI",
  "ChatDeployment": "gpt-4o-mini",
  "TimeoutSeconds": 60
}
```

#### 场景 2：全本地（Chat + Vision 都走 Ollama）

```json
"LlmProvider": "Ollama",
"ChatModel": "qwen3:8b",
"Vision": {
  "Enabled": true,
  "ModelProvider": "Ollama",
  "Model": "qwen2.5vl:3b",
  "TimeoutSeconds": 180
}
```

#### 场景 3：全云端（Chat + Vision 都走 Azure OpenAI，生产环境）

```json
"LlmProvider": "AzureOpenAI",
"AzureOpenAI": {
  "Endpoint": "...",
  "ApiKey": "...",
  "ChatDeployment": "gpt-4o-mini"
},
"Vision": {
  "Enabled": true
  // ModelProvider 不填，自动跟随 LlmProvider = AzureOpenAI
}
```

### A.5 appsettings.Development.json 变更

当前 Development 配置需增加 `ModelProvider` 字段，方便在本地随时切换：

```json
"Vision": {
  "Enabled": true,
  "ModelProvider": "Ollama",
  "Model": "qwen2.5vl:3b",
  "TimeoutSeconds": 180
}
```

### A.6 测试补充

| 测试用例 | 验证 |
|---------|------|
| `AddVedaAiServices_VisionAzure_LlmOllama_ShouldRegisterSeparateVisionClient` | `ModelProvider=AzureOpenAI` 时 "vision" 键不复用 Ollama chat |
| `AddVedaAiServices_VisionOllama_LlmAzure_ShouldRegisterOllamaVisionClient` | `ModelProvider=Ollama` 时 "vision" 键使用 Ollama 连接 |
| `VisionOptions_DefaultProvider_ShouldFollowLlmProvider` | 未配置 `ModelProvider` 时行为与原行为一致（向后兼容） |

### A.7 文件变更

| 文件 | 变更 |
|------|------|
| `Veda.Services/VisionOptions.cs` | 新增 `ModelProvider`、`ChatDeployment` 属性 |
| `Veda.Services/ServiceCollectionExtensions.cs` | Vision 注册段改为四路分发逻辑 |
| `appsettings.json` | Vision 节增加 `ModelProvider`、`ChatDeployment` 占位字段 |
| `appsettings.Development.json` | 按场景配置（开发默认仍走 Ollama） |
