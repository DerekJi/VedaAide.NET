# VedaAide 六期开发计划与实施路线

> 基准文档：[五期开发计划](./stage5-development-plan.cn.md)
>
> 形成背景：文档摄取层讨论（2026-03-30），聚焦文件内容提取管线的成本优化与鲁棒性提升。

**文档目标**：定义六期开发范围、模块设计、实施路线及验收标准，作为工程落地的参考基线。

---

## 1. 背景与目标

### 1.1 五期产出基础

VedaAide 五期已完成：

- Azure Entra External ID (CIAM) 多用户身份认证（JWT Bearer）
- `OwnerId` 从 Token 提取，后端强制校验，数据隔离可信
- 前端隐式 + 显式反馈 UX（复制 / 来源展开 / 👍👎）
- MCP 通道安全分层（公共只读 API Key / 用户私有 JWT）
- 示例文档库预置 + 文档浏览端点 `GET /api/documents`

**五期遗留问题（六期修复）：**

- `Evaluation`、`Prompts`、`Governance` 三个管理端点对所有登录用户开放，缺乏角色隔离
- Chat 页辅助 UI 标签（参考来源、幻觉警告、反馈按钮）硬编码中文，与回答语言不一致

### 1.2 六期目标

五期完成后，`DocumentIngestService` 的文件提取路由为：

```
RichMedia  →  VisionModelFileExtractor   (GPT-4o-mini Vision)
其他类型   →  DocumentIntelligenceFileExtractor   (Azure DI)
```

当前问题：

1. **成本结构不均衡**：Azure DI `prebuilt-invoice` 约 $10/千页，而 GPT-4o-mini Vision 约 $0.6/千张，差 15–20 倍。对个人应用场景，Azure DI 免费层（500 页/自然月）超出后成本陡升。
2. **无降级保护**：Azure DI 超出免费配额后直接抛异常（HTTP 429），摄取请求失败，无自动恢复。
3. **证件类模型选择不精准**：护照/身份证/驾照当前使用 `prebuilt-read`（通用 OCR），Azure DI 有更精准的 `prebuilt-idDocument` 专用模型，未使用。
4. **纯文字 PDF 走 OCR 管线**：纯文字层 PDF 不需要 OCR，直接提取文字层更快更准，但当前一律送 Azure DI 处理，浪费配额。

六期核心目标：

| 目标 | 优先级 | 说明 |
|------|--------|------|
| **Azure DI 配额感知 + 自动 Fallback** | P0 | 429 时自动降级到 GPT-4o-mini Vision，内存状态记录，下月自然月自动恢复 |
| **PDF 文字层直通提取** | P1 | 纯文字 PDF 跳过 OCR，直接提取文字层，节省配额和延迟 |
| **证件类专用模型** | P1 | `DocumentType.Identity` 路由到 `prebuilt-idDocument`，提升字段识别准确率 |
| **VisionFileExtractor Prompt 优化** | P2 | 针对发票/小票/证件的结构化提取 Prompt，输出质量对齐 Azure DI |
| **管理员角色权限隔离** | P1 | 基于 Entra ID App Roles，将 Evaluation/Prompts/Governance 限制为 Admin 角色专用 |
| **Chat 辅助文字语言跟随** | P2 | 参考来源、幻觉警告、反馈按钮等辅助标签随回答语言动态切换 |

---

## 2. 模块架构变化

六期变化集中在 `Veda.Services` 和 `Veda.Core`，上层 API 无感知：

```
┌─────────────────────────────────────────────────────────┐
│              Veda.Api / Veda.MCP                        │
│  变更：EvaluationController — 增加 [Authorize("Admin")] │
│  变更：PromptsController    — 增加 [Authorize("Admin")] │
│  变更：GovernanceController — 增加 [Authorize("Admin")] │
│  变更：Program.cs           — 注册 AdminOnly Policy     │
├─────────────────────────────────────────────────────────┤
│                   Veda.Services                         │
│  变更：DocumentIngestService — 增加 fallback 路由逻辑    │
│  变更：DocumentIntelligenceFileExtractor — 429 感知      │
│  新增：PdfTextLayerExtractor（纯文字 PDF 直通）           │
│  变更：VisionModelFileExtractor — 结构化 Prompt 优化      │
├─────────────────────────────────────────────────────────┤
│                   Veda.Core                             │
│  变更：DocumentType 枚举 — 新增 Identity 类型            │
├─────────────────────────────────────────────────────────┤
│                   Veda.Web（Angular）                   │
│  变更：AuthService — 新增 isAdmin() 方法读取 roles claim │
│  变更：app.component — Admin 菜单项条件显示              │
│  变更：chat.component / feedback-bar — 辅助标签语言跟随  │
└─────────────────────────────────────────────────────────┘
```

---

## 3. 功能模块详细设计

### 3.1 Azure DI 配额感知 + 自动 Fallback（P0）

#### 3.1.1 方案选择

| 方案 | 实现复杂度 | 多实例支持 | 月度自动重置 |
|------|-----------|-----------|-------------|
| 每次 429 → 立即 fallback | 最简，但每次都浪费一次 DI 请求 | ✓ | ✓ |
| **内存状态 + 到期时间（选定）** | 低 | 单实例 ✓（多实例各自独立） | 自动：截止时间 = 下月 1 日 UTC |
| Redis/CosmosDB 共享状态 | 高，引入额外依赖 | ✓ | 需定时任务 |

当前部署为单容器，选定**内存状态方案**。

#### 3.1.2 设计

`DocumentIntelligenceFileExtractor` 内部维护：

```
DateTime? _quotaExceededUntil   // null = 正常；非 null = 超限截止（下月 1 日 UTC）
```

调用逻辑：

```
1. 若 _quotaExceededUntil != null && UtcNow < 截止时间 → 抛出 QuotaExceededException
2. 调用 Azure DI
3. 若 catch RequestFailedException(429) → 设置 _quotaExceededUntil = 下月 1 日 00:00 UTC → 重抛 QuotaExceededException
```

`DocumentIngestService` 捕获 `QuotaExceededException`，自动降级：

```
try { docIntelExtractor.ExtractAsync() }
catch (QuotaExceededException) { visionExtractor.ExtractAsync() }
```

#### 3.1.3 新增类型

- `QuotaExceededException`（`Veda.Core` 或 `Veda.Services`，待定）：语义明确，区别于通用 `InvalidOperationException`

---

### 3.2 PDF 文字层直通提取（P1）

#### 3.2.1 背景

带文字层的 PDF（Word 导出、打印件）直接有可机读文本，无需 OCR。当前全部送 Azure DI 处理，浪费配额。

#### 3.2.2 方案

新增 `PdfTextLayerExtractor`，使用 **PdfPig**（`UglyToad.PdfPig`，MIT 协议，零外部依赖）：

- 打开 PDF → 遍历所有页 → 提取文本块（保留行序）
- 若提取字符数 < 阈值（如 100 字符/页），判定为扫描件，回退到 Azure DI / Vision

路由逻辑加入 `DocumentIngestService`：

```
application/pdf + 有文字层 → PdfTextLayerExtractor（直通）
application/pdf + 无文字层（扫描件）→ DocumentIntelligenceFileExtractor（OCR）
image/*            → DocumentIntelligenceFileExtractor / VisionModelFileExtractor（现有逻辑）
```

---

### 3.3 证件类专用模型（P1）

#### 3.3.1 DocumentType 扩展

`Veda.Core` 的 `DocumentType` 枚举新增：

```csharp
Identity,   // 护照 / 身份证 / 驾照
```

#### 3.3.2 路由变更

`DocumentIntelligenceFileExtractor` 的模型选择：

| DocumentType | Azure DI 模型 |
|---|---|
| `BillInvoice` | `prebuilt-invoice` |
| `Identity` | `prebuilt-idDocument` |
| 其余 | `prebuilt-read` |

---

### 3.4 Vision Prompt 优化（P2）

`VisionModelFileExtractor.BuildExtractionPrompt` 补充 `BillInvoice` 和 `Identity` 的结构化提取 Prompt，确保 Fallback 时输出质量接近 Azure DI 专用模型：

| DocumentType | Prompt 要点 |
|---|---|
| `BillInvoice` | 提取：商家名称、日期、各明细项（品名/数量/单价）、总金额、税额 |
| `Identity` | 提取：姓名、证件号、出生日期、签发机关、有效期，按字段名：字段值格式输出 |

---

### 3.5 管理员角色权限隔离（P1）

#### 3.5.1 背景与问题

`Evaluation`、`Prompts`、`Governance` 三个模块对系统稳定性和数据安全影响较大：
- `Prompts`：修改后立即影响所有用户的查询质量
- `Evaluation`：消耗 LLM Token，批量运行成本不可控
- `Governance`：可分配文档共享权限，涉及隐私隔离

五期未做角色隔离，任何登录用户均可访问，存在安全风险。

#### 3.5.2 方案：Entra ID App Roles（零额外存储）

利用现有 Entra External ID 基础设施，在 App Registration 中定义 `Admin` Role 并通过 JWT `roles` claim 传递，后端用 ASP.NET Core 内置 Policy 验证。

**优势：** 与现有 JWT Bearer 无缝集成，无需新增数据库表或自定义 Role 服务。

#### 3.5.3 实施细节

**① Azure Portal 配置（一次性操作）**

在 `VedaAide-API` App Registration → **App roles** → 创建：

| 字段 | 值 |
|------|----|
| Display name | `Admin` |
| Allowed member types | `Users/Groups` |
| Value | `Admin` |

在 Enterprise Applications → `VedaAide-API` → **Users and groups** 为管理员账号分配 `Admin` Role。

**② 后端：注册 Policy + 保护端点**

`Program.cs` 修改授权注册：

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
```

`EvaluationController`、`PromptsController`、`GovernanceController` 各加上：

```csharp
[Authorize(Policy = "AdminOnly")]
```

`HttpContextCurrentUserService` 新增：

```csharp
public bool IsAdmin =>
    accessor.HttpContext?.User.IsInRole("Admin") == true;
```

`ICurrentUserService` 接口同步新增 `bool IsAdmin { get; }`。

**③ 前端：菜单项条件显示**

`AuthService` 新增方法：

```typescript
isAdmin(): boolean {
  const roles = this.msalService.instance.getActiveAccount()
    ?.idTokenClaims?.['roles'] as string[] ?? [];
  return roles.includes('Admin');
}
```

`app.component.ts` 中 Prompts / Evaluation 菜单项加条件：

```html
@if (auth.isAdmin()) {
  <li><a routerLink="/prompts" routerLinkActive="active">...</a></li>
  <li><a routerLink="/evaluation" routerLinkActive="active">...</a></li>
}
```

---

### 3.6 Chat 辅助文字语言跟随（P2）

#### 3.6.1 背景与问题

Chat 页存在三处硬编码语言不一致的 UI 标签：

| 标签 | 当前语言 | 文件 |
|------|---------|------|
| `参考来源（N 处）` | 硬编码中文 | `chat.component.html` |
| `⚠ Possible hallucination` | 硬编码英文 | `chat.component.html` |
| `这个回答有帮助吗？` / `有用` / `没帮助` | 硬编码中文 | `feedback-bar.component.html` |

当系统回答英文内容时，辅助标签语言与答案不一致，体验割裂。

#### 3.6.2 方案：基于回答语言的标签字典

在前端维护一个轻量的标签字典，根据回答消息检测到的语言（`zh` / `en`）动态切换，**不引入 Angular i18n 框架**（避免过度工程）。

**标签字典（`chat-labels.ts`）：**

```typescript
export type Lang = 'zh' | 'en';

export const CHAT_LABELS: Record<Lang, {
  sources: (n: number) => string;
  hallucination: string;
  feedbackQuestion: string;
  helpful: string;
  notHelpful: string;
}> = {
  zh: {
    sources: (n) => `📎 参考来源（${n} 处）`,
    hallucination: '⚠ 可能存在幻觉',
    feedbackQuestion: '这个回答有帮助吗？',
    helpful: '👍 有用',
    notHelpful: '👎 没帮助',
  },
  en: {
    sources: (n) => `📎 Sources (${n})`,
    hallucination: '⚠ Possible hallucination',
    feedbackQuestion: 'Was this answer helpful?',
    helpful: '👍 Helpful',
    notHelpful: '👎 Not helpful',
  },
};
```

**语言检测（复用 `QueryService` 的中文判断逻辑）：**

```typescript
// 简单启发式：CJK Unicode 字符占比 > 20% 判定为中文
function detectLang(text: string): Lang {
  const cjk = (text.match(/[\u4e00-\u9fff]/g) ?? []).length;
  return cjk / text.length > 0.2 ? 'zh' : 'en';
}
```

`ChatMessage` 模型新增 `lang: Lang` 字段，在收到回答后检测并存储。`chat.component.html` 和 `feedback-bar.component.html` 改为绑定标签字典输出，替换所有硬编码字符串。

---

## 4. 数据模型变化

| 变更 | 范围 | 说明 |
|------|------|------|
| `DocumentType.Identity` 枚举值新增 | `Veda.Core` | 影响前端上传表单的类型选项，需同步更新 API DTO |
| `ICurrentUserService.IsAdmin` 属性新增 | `Veda.Core` | 从 JWT `roles` claim 读取，后端 Controller 可直接使用 |
| `ChatMessage.lang` 字段新增 | `Veda.Web` | 前端模型，不持久化，仅运行时使用 |
| 无 Schema 变更 | `Veda.Storage` | 存储层无需迁移 |

---

## 5. 实施路线

```
Week 1
  ├── P0 QuotaExceededException 定义
  ├── P0 DocumentIntelligenceFileExtractor 429 感知 + _quotaExceededUntil 内存状态
  └── P0 DocumentIngestService fallback 逻辑 + 单元测试

Week 2
  ├── P1 PdfPig 依赖引入 + PdfTextLayerExtractor 实现
  ├── P1 DocumentIngestService PDF 路由逻辑
  ├── P1 DocumentType.Identity + prebuilt-idDocument 路由
  └── P1 管理员 App Role：Azure Portal 配置 + 后端 Policy + Controller 保护 + 前端菜单隐藏

Week 3
  ├── P2 VisionModelFileExtractor Prompt 优化（BillInvoice / Identity）
  ├── P2 Chat 辅助文字语言跟随（chat-labels.ts + detectLang + 组件绑定）
  ├── 前端上传表单同步新增 Identity 类型选项
  └── 集成测试：发票/超市小票/护照/身份证/PDF 各场景 + 权限隔离 + 标签语言各场景验收
```

---

## 6. 验收标准

| 场景 | 验收条件 |
|------|---------|
| Azure DI 正常 | 发票/证件走 Azure DI，日志输出 `DocumentIntelligenceFileExtractor` |
| Azure DI 429 | 自动降级 Vision，日志输出 `Azure DI quota exceeded, falling back to Vision model` |
| 429 后再次请求（同月） | 直接走 Vision，不再请求 Azure DI，无额外延迟 |
| 次月 1 日后 | 自动恢复尝试 Azure DI |
| 纯文字 PDF | 走 `PdfTextLayerExtractor`，日志输出页数和字符数，不消耗 Azure DI 配额 |
| 扫描件 PDF | 文字层为空，回退 Azure DI / Vision |
| 护照/身份证上传（`Identity` 类型） | Azure DI 使用 `prebuilt-idDocument` 模型 |
| 普通用户访问 `/api/evaluation` | 返回 403，前端 Evaluation 菜单不可见 |
| 普通用户访问 `/api/prompts` | 返回 403，前端 Prompts 菜单不可见 |
| Admin 用户访问 Evaluation/Prompts | 正常响应，菜单可见 |
| 中文回答时辅助标签 | 参考来源、反馈按钮、幻觉警告均显示中文 |
| 英文回答时辅助标签 | 参考来源、反馈按钮、幻觉警告均显示英文 |

---

## 7. 依赖与风险

| 项 | 说明 |
|-----|------|
| `UglyToad.PdfPig` | MIT 协议，纯 .NET，无原生依赖，低风险 |
| `prebuilt-idDocument` | 仍在 Azure DI 免费配额内，无额外成本 |
| Vision fallback 成本 | GPT-4o-mini Vision ~$0.0006/张，远低于 Azure DI 超限单价，可接受 |
| 多实例部署 | 内存状态各实例独立，每实例第一次 429 才触发 fallback，可接受；若未来横向扩展明显，再考虑 Redis 共享状态 |
| Entra App Role 分配 | 需在 Azure Portal 手动操作，首次配置由管理员在 Enterprise Applications 完成；无代码风险 |
| 语言检测启发式误判 | CJK 占比阈值 20% 为经验值，极少数混合语言回答可能误判；误判仅影响辅助标签语言，不影响核心功能 |
