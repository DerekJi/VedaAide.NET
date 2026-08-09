# Resume JD Tailoring — Development Analysis

**Date:** 2026-04-03  
**Status:** Draft (pending review)

---

## 1. Requirements Overview

A user on the `resume` Angular frontend:

1. Enters a Job Description (JD) as **text or an image**;
2. The frontend calls **VedaAide.NET's MCP** (Model Context Protocol);
3. VedaAide.NET combines the resume material already stored in the knowledge base to generate a **Markdown resume tailored to that JD**.

---

## 2. Users and Scenarios Revisited

| User | Scenario | Login required? |
|------|------|------|
| **Recruiter** | Visits `derekji.github.io`, enters a JD, quickly previews an AI-tailored resume | ❌ No login |
| **Derek (owner)** | Uses the full VedaAide feature set on a daily basis | ✅ Entra ID login |

> The resume site is a public business card; "AI-generated resume" is a **login-free highlight feature** on it, meant to leave a strong impression on recruiters rather than pushing them through an account registration flow.

---

## 3. Security Approach: Dedicated Endpoints + Double Abuse Protection

### 3.1 Core Idea

VedaAide.NET exposes two dedicated public endpoints for the resume site:

```
GET  /api/public/resume/ping    # lightweight health probe so the frontend can detect when cold start finishes
POST /api/public/resume/tailor  # stream a tailored resume (SSE)
```

These two endpoints:
- Are `[AllowAnonymous]` — no JWT, no login required;
- Only accept cross-origin requests from the resume site's origin (CORS allowlist);
- Use dedicated strict rate limiting (per-IP) to prevent LLM quota abuse;
- Only retrieve resume fragments with `Visibility=Public` (content Derek explicitly made public, no private fields such as phone numbers).

### 3.2 Abuse Protection Mechanisms

> CORS is just a browser-level courtesy convention; it cannot stop curl/Postman. **The real protection is rate limiting.**

| Mechanism | Configuration | Purpose |
|------|------|------|
| **CORS allowlist** | Only `https://derekji.github.io` (dev: `localhost:4200`) | Prevents other sites from embedding/calling (browser layer) |
| **Per-IP fixed-window rate limit** | New `resume-public` policy; quota via `Veda:PublicResume:RateLimit` (default: production 5/hour, local dev 30/hour) | Core protection: limits crawlers and script abuse |
| **Request body size limit** | JD text ≤ 4000 chars | Prevents oversized-prompt attacks |
| **Global rate-limit fallback** | Existing `global` policy: 60/minute | Final safety net |

Already in place (no rebuild needed):
- CORS: `AddCors` + `AllowedOrigins` config exists; just add a `ResumePublicPolicy`;
- Rate limiting: `AddRateLimiter` exists; just add the `resume-public` per-IP policy.

### 3.3 Public/Private Layering of Resume Data

**Two resume assets, different purposes:**

| Document | Visibility | Content | Used by |
|------|------|------|------|
| `derek-resume-public.md` | `Public` (no OwnerId) | Public version with private fields (phone, home address) removed | Recruiter-facing endpoint `/api/public/resume/tailor` |
| `derek-resume-private.md` | `Private` (OwnerId = Derek's OID) | Full resume, including contact details | Derek himself in VedaAide.Web (future extension) |

This way, even if someone extracts the API address and calls it directly, they only get content Derek explicitly made public — no private data leaks.

### 3.4 Why Not a "Resume-Site-Specific API Key"

The Angular bundle is public, so an API Key can always be extracted (no matter how it is obfuscated) — it is effectively public.  
In that case, just use `[AllowAnonymous]` + IP rate limiting: no key-management overhead, equivalent security.

---

## 4. Feasibility Assessment

**It can be done.** All core dependencies are already in place:

| Dependency | Current state |
|------|------|
| VedaAide.NET knowledge base | Already supports vector search + `KnowledgeScope(Visibility)` filtering; ingest resume material as `Public` |
| VedaAide.NET LLM capabilities | Already has `ChatModel` (Ollama / Azure GPT-4o-mini / DeepSeek); SSE streaming has a ready example |
| CORS | `AddCors` + `AllowedOrigins` config exists; just add `ResumePublicPolicy` |
| Rate Limiter | `AddRateLimiter` exists; just add a per-IP policy |
| resume frontend | Angular 21, native `fetch` + `ReadableStream` to consume SSE, no extra library needed |

---

## 5. Overall Flow

```
Recruiter (no login)
  │
  │  ① Enter JD text on derekji.github.io
  ▼
resume (Angular)
  │
  │  ② POST /api/public/resume/tailor
  │     { "jobDescription": "...", "maxChars": 4000 }
  ▼
VedaAide.NET (Veda.Api)
  │
  ├── ③ Request validation: CORS allowlist + per-IP rate limit (5/hour) + char limit
  ├── ④ Vector search: retrieve resume fragments with Visibility=Public
  ├── ⑤ Build prompt (JD + public resume fragments, strictly no fabrication)
  └── ⑥ Call LLM, stream Markdown resume back via SSE
  ▼
resume (Angular)
  ⑦ Stream-render Markdown, provide a download button
```

---

## 6. What Each Side Needs to Do

### 6.1 VedaAide.NET Side

#### 6.1.1 Preprocessing: Ingest the Public Resume Material

Organize the resume content into `derek-resume-public.md` (**remove private fields such as phone number and home address**), then write it into the knowledge base via `/api/admin/ingest`:

```csharp
await documentIngestor.IngestAsync(
    content:      markdownContent,
    documentName: "derek-resume-public.md",
    documentType: DocumentType.Other,
    scope: new KnowledgeScope(Visibility: Visibility.Public)  // no OwnerId — public
);
```

Re-ingest after every resume content update.

#### 6.1.2 New Dedicated Controller

Add `PublicResumeTailorController` under `Veda.Api/Controllers/`:

```csharp
[ApiController]
[Route("api/public/resume")]
[AllowAnonymous]                                    // no JWT required
[EnableCors("ResumePublicPolicy")]                  // dedicated CORS policy
[EnableRateLimiting("resume-public")]               // dedicated rate-limit policy
public class PublicResumeTailorController(...) : ControllerBase
{
    [HttpPost("tailor")]
    public async Task Tailor([FromBody] PublicTailorRequest request, CancellationToken ct)
    {
        // 1. Validate request.JobDescription length ≤ 4000 chars
        // 2. Vector search for resume fragments with Visibility=Public
        // 3. Build prompt → LLM → SSE streaming response
    }
}

public record PublicTailorRequest(
    [MaxLength(4000)] string JobDescription,
    int TopK = 8);
```

#### 6.1.3 New CORS Policy `ResumePublicPolicy`

Append to `AddCors` in `Program.cs`:

```csharp
options.AddPolicy("ResumePublicPolicy", policy =>
    policy.WithOrigins("https://derekji.github.io", "http://localhost:4200")
          .WithMethods("POST")
          .WithHeaders("Content-Type"));
```

#### 6.1.4 New Rate-Limit Config Option `PublicResumeOptions`

Rate-limit parameters come from appsettings to avoid hardcoding:

```json
// appsettings.json (production defaults)
"Veda": {
  "PublicResume": {
    "RateLimitPerIpPerHour": 5,
    "MaxJobDescriptionChars": 4000
  }
}

// appsettings.Development.json (relaxed locally)
"Veda": {
  "PublicResume": {
    "RateLimitPerIpPerHour": 30
  }
}
```

Append to `AddRateLimiter` in `Program.cs`, reading the config before registering the policy:

```csharp
var publicResumeOpts = cfg.GetSection("Veda:PublicResume").Get<PublicResumeOptions>() ?? new();

// fixed-window rate limit partitioned by source IP
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

#### 6.1.5 Exempt `/api/public/*` in ApiKeyMiddleware

```csharp
// Add to ApiKeyMiddleware.IsExcluded():
|| path.StartsWith("/api/public", StringComparison.OrdinalIgnoreCase)
```

---

### 6.2 resume (Angular) Side

#### 6.2.1 New Section: `JobTailorComponent`

A standalone section inside the existing SPA (no routing); suggested placement after Experience or Hero.

Page features:
- **JD input area**: a `<textarea>` for pasting the JD, with a character counter (max 4000);
- **Generate button**: triggers the API call;
- **Streaming output area**: uses `fetch` + `ReadableStream` to accumulate tokens and render in real time;
- **Download button**: saves the Markdown content as a `resume-tailored.md` file.

Suggested layout:
```
src/app/pages/job-tailor/
  job-tailor.module.ts
  job-tailor.component.ts
  job-tailor.component.html
  job-tailor.component.scss
```

#### 6.2.2 New Service: `TailorService`

```typescript
// src/app/core/tailor.service.ts
@Injectable({ providedIn: 'root' })
export class TailorService {
  private readonly endpoint = `${environment.vedaApiUrl}/api/public/resume/tailor`;

  tailor(jobDescription: string): Observable<string> {
    // consume SSE via fetch() + ReadableStream, emit progressively accumulated Markdown
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

#### 6.2.3 Environment Configuration

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

#### 6.2.4 Dependencies

- Markdown rendering: `marked` (lightweight, framework-agnostic) — `pnpm add marked @types/marked`;
- No auth library needed (MSAL): the endpoint is `[AllowAnonymous]`, no JWT required.

---

## 7. Development Steps (Phased)

### Phase 1 — Text JD → Streamed Markdown Resume (~2-3 days)

**VedaAide.NET:**
1. Prepare `derek-resume-public.md` and ingest it with `Visibility=Public` via the admin API;
2. Add the `IPublicResumeTailoringService` interface and its implementation in `Veda.Services`;
3. Add the `ResumePublicPolicy` CORS policy + `resume-public` per-IP rate limiting;
4. Add `PublicResumeTailorController` (`[AllowAnonymous]`, SSE response);
5. Exempt `/api/public/*` in `ApiKeyMiddleware`.

**resume (Angular):**
1. Add `environment.vedaApiUrl`;
2. Add `TailorService` (`fetch` + `ReadableStream`);
3. Add `JobTailorModule` + Component (text input, streamed rendering, download);
4. Register in `app.module.ts`, insert the section in a suitable spot in `app.component.html`.

### Phase 2 — Image Input Support (~1-2 days, optional)

**VedaAide.NET:**
- Add a `multipart/form-data` variant to `PublicResumeTailorController`;
- Reuse `VisionOptions` (Azure Computer Vision) or `DocumentIntelligenceOptions` to extract text from the image;
- After extraction, follow the same flow as Phase 1.

**resume (Angular):**
- Add `<input type="file" accept="image/*">` to `JobTailorComponent`;
- Add a `tailorFromImage(file: File)` method to `TailorService`.

---

## 8. Key Risks and Notes

| Risk | Mitigation |
|------|------|
| **LLM quota abuse** | Per-IP rate limit (production default 5/hour, configurable via `Veda:PublicResume:RateLimitPerIpPerHour`); global rate-limit fallback |
| **Private data leakage** | Only ingest `derek-resume-public.md` (no phone/address); Private documents never participate in this endpoint's search |
| **Accidentally ingesting with Private scope** | The public document must use `Visibility=Public` with no OwnerId; pin this down in the ingest script to avoid manual mistakes |
| **LLM fabrication** | Strong System Prompt constraint: "use only the provided context, do not invent any information" |
| **Missing CORS config** | Both `derekji.github.io` and `localhost:4200` must be in the `ResumePublicPolicy` allowlist |
| **Image OCR cost** | Azure Document Intelligence bills per page; cap upload size at ≤ 2MB; not implemented in Phase 1 |
| **Distributed botnets bypassing IP limits** | Future option: integrate **Cloudflare Turnstile** (free, invisible to users); the frontend sends a challenge token that the backend verifies before running generation. Not implemented in Phase 1; decide based on actual abuse |
