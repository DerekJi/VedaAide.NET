# Resume JD Tailoring — 开发分析文档

**Date:** 2026-04-03  
**Status:** 草稿（待评审）

---

## 1. 需求概述

用户在 `resume` Angular 前端：

1. 以**文字或图片**形式输入一份 Job Description（JD）；
2. 前端调用 **VedaAide.NET 的 MCP**（Model Context Protocol）；
3. VedaAide.NET 结合知识库中已有的简历素材，生成**适合该 JD 的 Markdown 格式简历**。

---

## 2. 用户与场景重新定位

| 用户 | 场景 | 是否需要登录 |
|------|------|------|
| **招聘方** | 访问 `derekji.github.io`，输入 JD，快速看一眼 AI 定制简历 | ❌ 无需登录 |
| **Derek 本人** | 日常使用 VedaAide 完整功能 | ✅ Entra ID 登录 |

> resume 站是公开名片，"AI 生成简历"是其上一个**免登录的亮点 feature**，目的是让招聘方留下深刻印象，而不是让他们走流程注册账号。

---

## 3. 安全方案：专供端点 + 双重防滥用

### 3.1 核心思路

VedaAide.NET 为 resume 站提供两个专供公开接口：

```
GET  /api/public/resume/ping    # 轻量健康探针，用于前端检测冷启动是否完成
POST /api/public/resume/tailor  # 流式生成定制简历（SSE）
```

这两个接口：
- `[AllowAnonymous]` — 不要求 JWT，无需登录；
- 仅允许来自 resume 站 origin 的跨域请求（CORS 白名单）；
- 专用严格限流（per-IP），防止 LLM 配额滥用；
- 只检索 `Visibility=Public` 的简历片段（Derek 主动公开的内容，无手机号等隐私字段）。

### 3.2 防滥用机制

> CORS 只是浏览器层的礼貌约定，无法阻止 curl/Postman。**真正的防护是限流。**

| 机制 | 配置 | 作用 |
|------|------|------|
| **CORS 白名单** | 只允许 `https://derekji.github.io`（dev: `localhost:4200`） | 阻止其他网站嵌入/调用（浏览器层） |
| **Per-IP 固定窗口限流** | 新增 `resume-public` policy，限额通过 `Veda:PublicResume:RateLimit` 配置（默认：生产 5次/小时，本地开发 30次/小时） | 核心防护：限制爬虫和脚本滥用 |
| **请求体大小限制** | JD 文本 ≤ 4000 字符 | 防止超长 Prompt 攻击 |
| **全局限流兜底** | 现有 `global` policy：60 次/分钟 | 最终兜底 |

已有基础（无需重建）：
- CORS：`AddCors` + `AllowedOrigins` 配置已有，新增一条 `ResumePublicPolicy` 即可；
- 限流：`AddRateLimiter` 已有，新增 `resume-public` per-IP 策略即可。

### 3.3 简历数据的公开/私有分层

**两份简历素材，用途不同：**

| 文档 | Visibility | 内容 | 用于 |
|------|------|------|------|
| `derek-resume-public.md` | `Public`（无 OwnerId） | 去掉手机号、家庭住址等隐私字段的公开版 | 招聘方专供端点 `/api/public/resume/tailor` |
| `derek-resume-private.md` | `Private`（OwnerId = Derek's OID） | 完整简历，含联系方式 | Derek 自己在 VedaAide.Web 使用（未来扩展） |

这样即使有人提取到 API 地址直接调用，也只能得到 Derek 主动公开的内容，不会泄露任何隐私数据。

### 3.4 为什么不用"resume 站专用 API Key"

Angular bundle 是公开的，API Key 必然可被提取（无论如何混淆），等同于公开。  
既然如此，不如直接 `[AllowAnonymous]` + IP 限流，省去密钥管理负担，同等安全效果。

---

## 4. 可行性判断

**可以做到。** 核心依赖已全部就位：

| 依赖 | 现状 |
|------|------|
| VedaAide.NET 知识库 | 已支持向量搜索 + `KnowledgeScope(Visibility)` 过滤；简历素材 ingest 为 `Public` 即可 |
| VedaAide.NET LLM 能力 | 已有 `ChatModel`（Ollama / Azure GPT-4o-mini / DeepSeek）；SSE 流式输出有现成范例 |
| CORS | `AddCors` + `AllowedOrigins` 配置已有，新增 `ResumePublicPolicy` 即可 |
| Rate Limiter | `AddRateLimiter` 已有，新增 per-IP 策略即可 |
| resume 前端 | Angular 21，原生 `fetch` + `ReadableStream` 消费 SSE，无需额外库 |

---

## 5. 整体流程

```
招聘方（无需登录）
  │
  │  ① 在 derekji.github.io 输入 JD 文字
  ▼
resume (Angular)
  │
  │  ② POST /api/public/resume/tailor
  │     { "jobDescription": "...", "maxChars": 4000 }
  ▼
VedaAide.NET (Veda.Api)
  │
  ├── ③ 请求校验：CORS 白名单 + per-IP 限流（5次/小时）+ 字符数上限
  ├── ④ 向量搜索：检索 Visibility=Public 的简历片段
  ├── ⑤ 构建 Prompt（JD + 公开简历片段，严禁虚构）
  └── ⑥ 调用 LLM，以 SSE 流式返回 Markdown 简历
  ▼
resume (Angular)
  ⑦ 流式渲染 Markdown，提供下载按钮
```

---

## 6. 两边各需要做什么

### 6.1 VedaAide.NET 侧

#### 6.1.1 预处理：ingest 公开版简历素材

将简历内容整理为 `derek-resume-public.md`（**去掉手机号、家庭住址等隐私字段**），通过 `/api/admin/ingest` 写入知识库：

```csharp
await documentIngestor.IngestAsync(
    content:      markdownContent,
    documentName: "derek-resume-public.md",
    documentType: DocumentType.Other,
    scope: new KnowledgeScope(Visibility: Visibility.Public)  // 无 OwnerId —— 公开
);
```

每次简历内容更新后需重新 ingest。

#### 6.1.2 新增专供 Controller

在 `Veda.Api/Controllers/` 新增 `PublicResumeTailorController`：

```csharp
[ApiController]
[Route("api/public/resume")]
[AllowAnonymous]                                    // 无需 JWT
[EnableCors("ResumePublicPolicy")]                  // 专用 CORS 策略
[EnableRateLimiting("resume-public")]               // 专用限流策略
public class PublicResumeTailorController(...) : ControllerBase
{
    [HttpPost("tailor")]
    public async Task Tailor([FromBody] PublicTailorRequest request, CancellationToken ct)
    {
        // 1. 校验 request.JobDescription 长度 ≤ 4000 字符
        // 2. 向量搜索 Visibility=Public 的简历片段
        // 3. 构建 Prompt → LLM → SSE 流式响应
    }
}

public record PublicTailorRequest(
    [MaxLength(4000)] string JobDescription,
    int TopK = 8);
```

#### 6.1.3 新增 CORS 策略 `ResumePublicPolicy`

在 `Program.cs` `AddCors` 中追加：

```csharp
options.AddPolicy("ResumePublicPolicy", policy =>
    policy.WithOrigins("https://derekji.github.io", "http://localhost:4200")
          .WithMethods("POST")
          .WithHeaders("Content-Type"));
```

#### 6.1.4 新增限流配置项 `PublicResumeOptions`

限流参数通过 appsettings 配置，避免硬编码：

```json
// appsettings.json（生产默认值）
"Veda": {
  "PublicResume": {
    "RateLimitPerIpPerHour": 5,
    "MaxJobDescriptionChars": 4000
  }
}

// appsettings.Development.json（本地放宽）
"Veda": {
  "PublicResume": {
    "RateLimitPerIpPerHour": 30
  }
}
```

在 `Program.cs` `AddRateLimiter` 中追加，读取配置后再注册策略：

```csharp
var publicResumeOpts = cfg.GetSection("Veda:PublicResume").Get<PublicResumeOptions>() ?? new();

// 按来源 IP 分区的固定窗口限流
options.AddPolicy("resume-public", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = publicResumeOpts.RateLimitPerIpPerHour,
            Window      = TimeSpan.FromHours(1),
            QueueLimit  = 0
        }));
```

#### 6.1.5 ApiKeyMiddleware 豁免 `/api/public/*`

```csharp
// ApiKeyMiddleware.IsExcluded() 中追加：
|| path.StartsWith("/api/public", StringComparison.OrdinalIgnoreCase)
```

---

### 6.2 resume (Angular) 侧

#### 6.2.1 新增 Section：`JobTailorComponent`

作为现有 SPA 内的一个独立 section（无路由），位置建议在 Experience 或 Hero 之后。

页面功能：
- **JD 输入区**：`<textarea>` 输入文字 JD，字符计数显示（上限 4000）；
- **生成按钮**：触发 API 调用；
- **流式输出区**：用 `fetch` + `ReadableStream` 逐 token 拼接，实时渲染；
- **下载按钮**：将 Markdown 内容保存为 `resume-tailored.md` 文件。

目录建议：
```
src/app/pages/job-tailor/
  job-tailor.module.ts
  job-tailor.component.ts
  job-tailor.component.html
  job-tailor.component.scss
```

#### 6.2.2 新增 Service：`TailorService`

```typescript
// src/app/core/tailor.service.ts
@Injectable({ providedIn: 'root' })
export class TailorService {
  private readonly endpoint = `${environment.vedaApiUrl}/api/public/resume/tailor`;

  tailor(jobDescription: string): Observable<string> {
    // 使用 fetch() + ReadableStream 消费 SSE，返回逐步累积的 Markdown
    return new Observable(observer => {
      fetch(this.endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ jobDescription })
      }).then(async res => {
        const reader = res.body!.getReader();
        const decoder = new TextDecoder();
        let accumulated = '';
        while (true) {
          const { done, value } = await reader.read();
          if (done) { observer.complete(); break; }
          accumulated += decoder.decode(value, { stream: true });
          observer.next(accumulated);
        }
      }).catch(err => observer.error(err));
    });
  }
}
```

#### 6.2.3 环境配置

```typescript
// environment.ts
export const environment = {
  production: false,
  vedaApiUrl: 'http://localhost:5000'
};

// environment.prod.ts
export const environment = {
  production: true,
  vedaApiUrl: 'https://<your-veda-api-domain>'
};
```

#### 6.2.4 依赖

- Markdown 渲染：`marked`（轻量，无框架依赖）— `pnpm add marked @types/marked`；
- 无需认证库（MSAL）：`[AllowAnonymous]` 端点，无 JWT 要求。

---

## 7. 开发步骤（分期）

### Phase 1 — 文字 JD → 流式 Markdown 简历（约 2-3 天）

**VedaAide.NET：**
1. 整理 `derek-resume-public.md`，通过 admin API 以 `Visibility=Public` ingest；
2. 在 `Veda.Services` 新增 `IPublicResumeTailoringService` 接口及实现；
3. 新增 `ResumePublicPolicy` CORS 策略 + `resume-public` per-IP 限流；
4. 新增 `PublicResumeTailorController`（`[AllowAnonymous]`，SSE 响应）；
5. `ApiKeyMiddleware` 豁免 `/api/public/*`。

**resume (Angular)：**
1. 新增 `environment.vedaApiUrl`；
2. 新增 `TailorService`（`fetch` + `ReadableStream`）；
3. 新增 `JobTailorModule` + Component（文字输入、流式渲染、下载）；
4. 注册到 `app.module.ts`，在 `app.component.html` 合适位置插入 section。

### Phase 2 — 图片输入支持（约 1-2 天，可选）

**VedaAide.NET：**
- `PublicResumeTailorController` 新增 `multipart/form-data` 变体；
- 复用 `VisionOptions`（Azure Computer Vision）或 `DocumentIntelligenceOptions` 提取图片文字；
- 提取后走 Phase 1 相同流程。

**resume (Angular)：**
- `JobTailorComponent` 新增 `<input type="file" accept="image/*">`；
- `TailorService` 新增 `tailorFromImage(file: File)` 方法。

---

## 8. 关键风险与注意事项

| 风险 | 应对 |
|------|------|
| **LLM 配额滥用** | per-IP 限流（生产默认 5次/小时，通过 `Veda:PublicResume:RateLimitPerIpPerHour` 配置）；全局限流兜底 |
| **隐私数据泄露** | 只 ingest `derek-resume-public.md`（无手机/住址）；Private 文档不参与此端点的搜索 |
| **ingest 时误用 Private Scope** | public 版文档必须用 `Visibility=Public`，无 OwnerId；通过 ingest 脚本固化，避免手动失误 |
| **LLM 虚构内容** | System Prompt 强约束："仅使用提供的上下文，不得捏造任何信息" |
| **CORS 配置遗漏** | `derekji.github.io` 和 `localhost:4200` 均需在 `ResumePublicPolicy` 白名单中 |
| **图片 OCR 费用** | Azure Document Intelligence 按页计费；限制上传文件大小 ≤ 2MB，Phase 1 暂不实现 |
| **分布式 botnet 绕过 IP 限流** | 后续可选：接入 **Cloudflare Turnstile**（免费，用户无感）；前端加 challenge token，后端验证后才执行生成逻辑。当前 Phase 1 不实现，视实际滥用情况再决策 |
