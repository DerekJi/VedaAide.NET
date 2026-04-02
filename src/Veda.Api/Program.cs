using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Veda.Agents;
using Veda.Api.GraphQL;
using Veda.Api.Middleware;
using Veda.Api.Services;
using Veda.Evaluation;
using Veda.MCP;
using Veda.Prompts;
using Veda.Services;
using Veda.Services.DataSources;
using Veda.Storage;

var builder = WebApplication.CreateBuilder(args);

// Re-add User Secrets AFTER environment variables so that secrets take priority over env vars.
// (ASP.NET Core default order: appsettings → env vars → user secrets in Dev;
//  by re-adding here we push secrets to the top of the stack in all environments.)
builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: false);

var cfg = builder.Configuration;

// ── AI Services (Embedding + LLM, provider decided by config) ───────────────
builder.Services.AddVedaAiServices(cfg);

// ── RAG Options (thresholds / feature flags) ──────────────────────────────────
builder.Services.Configure<RagOptions>(cfg.GetSection("Veda:Rag"));
builder.Services.Configure<VedaOptions>(cfg.GetSection("Veda"));

// ── Multimodal file extraction options ────────────────────────────────────────
builder.Services.Configure<DocumentIntelligenceOptions>(cfg.GetSection("Veda:DocumentIntelligence"));
builder.Services.Configure<VisionOptions>(cfg.GetSection("Veda:Vision"));

// ── Sprint 3: Semantics (personal vocabulary enhancer) ───────────────────────
builder.Services.Configure<SemanticsOptions>(cfg.GetSection("Veda:Semantics"));

// ── Storage (SQLite or CosmosDB, decided by Veda:StorageProvider) ───────────
builder.Services.AddVedaStorage(cfg);

// ── Prompts (Context Window Builder) ─────────────────────────────────────────
builder.Services.AddVedaPrompts();

// ── Agents (Orchestration) ────────────────────────────────────────────────────
builder.Services.AddVedaAgents();
// ── Evaluation (Phase 6) ──────────────────────────────────────────────────────
builder.Services.AddVedaEvaluation();
// ── Data Sources (MCP Client: file system + blob storage connectors) ────────
builder.Services.Configure<FileSystemConnectorOptions>(cfg.GetSection("Veda:DataSources:FileSystem"));
builder.Services.AddScoped<IDataSourceConnector, FileSystemConnector>();
builder.Services.Configure<BlobStorageConnectorOptions>(cfg.GetSection("Veda:DataSources:BlobStorage"));
builder.Services.AddScoped<IDataSourceConnector, BlobStorageConnector>();
builder.Services.AddScoped<IDemoLibraryService, DemoLibraryService>();
// Background auto-sync
builder.Services.Configure<DataSourceSyncOptions>(cfg.GetSection("Veda:DataSources:AutoSync"));
builder.Services.AddHostedService<Veda.Api.DataSourceSyncBackgroundService>();
// ── MCP Server (Knowledge Base Tools over HTTP/SSE) ──────────────────────────
builder.Services.AddVedaMcp();

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── Current User (Sprint 1: Entra ID JWT identity) ─────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

// ── JWT Bearer Authentication (Microsoft Entra ID) ──────────────────────────────────
// Validates JWT tokens issued by the configured Entra ID tenant.
// When AzureAd config is absent, operates in pass-through / anonymous mode.
builder.Services.Configure<AzureAdOptions>(cfg.GetSection("AzureAd"));
var entra = cfg.GetSection("AzureAd").Get<AzureAdOptions>() ?? new AzureAdOptions();
var entraAudience = entra.Audience ?? entra.ClientId;

if (!string.IsNullOrWhiteSpace(entra.TenantId) && !string.IsNullOrWhiteSpace(entra.ClientId))
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // For CIAM, the OIDC metadata is served under the domain-based URL.
            // The token 'iss' claim uses tenantId format, so we disable issuer
            // validation and rely on audience + signing key validation instead.
            options.Authority = !string.IsNullOrWhiteSpace(entra.Domain)
                ? $"{entra.Instance.TrimEnd('/')}/{entra.Domain}/v2.0/"
                : $"{entra.Instance.TrimEnd('/')}/{entra.TenantId}/v2.0/";
            options.Audience  = entraAudience;
            // Disable claim type remapping so CIAM claims (oid, sub, name, roles)
            // are preserved verbatim from the JWT rather than being mapped to
            // the long System.Security.Claims URI form.
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidateIssuer = false;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.NameClaimType = "name";
            // Accept both bare ClientId and api://ClientId audience formats,
            // since CIAM access tokens carry the plain GUID as 'aud'.
            options.TokenValidationParameters.ValidAudiences =
            [
                entraAudience,
                $"api://{entraAudience}",
                entra.ClientId!,
                $"api://{entra.ClientId}"
            ];
        });
}
else
{
    // No Entra ID config: register no-op auth so UseAuthentication() doesn't throw.
    builder.Services.AddAuthentication().AddJwtBearer();
}
builder.Services.AddAuthorization(options =>
{
    // AdminOnly: JWT roles claim 包含 "Admin"（Entra ID App Roles），
    // 或 oid/sub claim 在 AzureAd:AdminOids 白名单中（适合 CIAM token 无 roles claim 的场景）。
    var adminOids = entra.AdminOids;
    options.AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            ctx.User.IsInRole("Admin")
            || (ctx.User.FindFirst("oid")?.Value is string oid
                && adminOids.Contains(oid, StringComparer.OrdinalIgnoreCase))
            || (ctx.User.FindFirst("sub")?.Value is string sub
                && adminOids.Contains(sub, StringComparer.OrdinalIgnoreCase))));
});

// ── Health Checks ─────────────────────────────────────────────────────────────
var healthChecks = builder.Services.AddHealthChecks();
var vedaOpts = cfg.GetSection("Veda").Get<VedaOptions>() ?? new VedaOptions();
if (vedaOpts.StorageProvider.Equals("CosmosDb", StringComparison.OrdinalIgnoreCase))
    healthChecks.AddCheck<Veda.Api.HealthChecks.CosmosDbHealthCheck>("cosmosdb");
if (vedaOpts.EmbeddingProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
    healthChecks.AddCheck<Veda.Api.HealthChecks.AzureOpenAIConfigHealthCheck>("azure-openai");

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "VedaAide API", Version = "v1" });
});
// ── CORS ───────────────────────────────────────────────────────────────
var allowedOrigins = vedaOpts.Security.AllowedOrigins
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
    options.AddPolicy("VedaCorsPolicy", policy =>
    {
        if (allowedOrigins.Contains("*"))
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
    }));

// ── Rate Limiting (固定窗口，60 次/分钟/全局) ────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("global", policy =>
    {
        policy.PermitLimit = 60;
        policy.Window      = TimeSpan.FromMinutes(1);
        policy.QueueLimit  = 0;
    });
    options.RejectionStatusCode = 429;
});
// ── GraphQL (HotChocolate) ────────────────────────────────────────────────────
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

var app = builder.Build();

// ── Auto-migrate DB on startup ────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VedaDbContext>();
    try
    {
        await db.Database.MigrateAsync();
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("already exists"))
    {
        // DB was created by an older migration set; __EFMigrationsHistory may be
        // missing or inconsistent with the new InitialSchema migration.
        // Delete and recreate so the container starts cleanly.
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }
}

// ── CosmosDB container initialisation (only when StorageProvider=CosmosDb) ───
// Run in background so Kestrel starts serving requests immediately.
// The CosmosDbHealthCheck will report Degraded until init completes.
var cosmosInitializer = app.Services.GetService<Veda.Storage.CosmosDbInitializer>();
if (cosmosInitializer is not null)
{
    var appLogger = app.Services.GetRequiredService<ILogger<Program>>();
    _ = Task.Run(async () =>
    {
        using var initCts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try
        {
            await cosmosInitializer.EnsureReadyAsync(initCts.Token);
        }
        catch (Exception ex)
        {
            appLogger.LogWarning(ex,
                "CosmosDB initialisation failed (type={ExType}, msg={Msg}). " +
                "Check Managed Identity role assignments and CosmosDB endpoint.",
                ex.GetType().Name, ex.Message);
        }
    });
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// Log all unhandled exceptions; only expose details in Development
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var log = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    log.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    var isDev = ctx.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
    await ctx.Response.WriteAsJsonAsync(isDev
        ? (object)new { error = ex?.GetType().Name, message = ex?.Message }
        : new { error = "InternalServerError", message = "An unexpected error occurred." });
}));
app.UseCors("VedaCorsPolicy");
app.UseDefaultFiles();   // serve index.html for "/"
app.UseStaticFiles();    // serve Angular build from wwwroot
app.UseRateLimiter();
app.UseAuthentication();  // JWT Bearer — must come before ApiKeyMiddleware
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthorization();
app.MapHealthChecks("/health");  // Public, excluded from ApiKeyMiddleware
app.MapControllers();
app.MapGraphQL();   // GraphQL endpoint: /graphql (Banana Cake Pop UI in dev)
app.MapVedaMcp();   // MCP endpoint: /mcp (SSE transport for Copilot Chat)
app.MapFallbackToFile("index.html");  // SPA routing fallback

app.Run();
