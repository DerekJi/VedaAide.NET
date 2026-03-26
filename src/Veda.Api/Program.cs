using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Veda.Agents;
using Veda.Api.GraphQL;
using Veda.Api.Middleware;
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
// Background auto-sync
builder.Services.Configure<DataSourceSyncOptions>(cfg.GetSection("Veda:DataSources:AutoSync"));
builder.Services.AddHostedService<Veda.Api.DataSourceSyncBackgroundService>();
// ── MCP Server (Knowledge Base Tools over HTTP/SSE) ──────────────────────────
builder.Services.AddVedaMcp();

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── Health Checks ─────────────────────────────────────────────────────────────
var healthChecks = builder.Services.AddHealthChecks();
var storageProvider  = cfg["Veda:StorageProvider"]   ?? "Sqlite";
var embeddingProvider = cfg["Veda:EmbeddingProvider"] ?? "Ollama";
if (storageProvider.Equals("CosmosDb", StringComparison.OrdinalIgnoreCase))
    healthChecks.AddCheck<Veda.Api.HealthChecks.CosmosDbHealthCheck>("cosmosdb");
if (embeddingProvider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
    healthChecks.AddCheck<Veda.Api.HealthChecks.AzureOpenAIConfigHealthCheck>("azure-openai");

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "VedaAide API", Version = "v1" });
});
// ── CORS ───────────────────────────────────────────────────────────────
var allowedOrigins = (cfg["Veda:Security:AllowedOrigins"] ?? "*").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
    await db.Database.MigrateAsync();
}

// ── CosmosDB container initialisation (only when StorageProvider=CosmosDb) ───
var cosmosInitializer = app.Services.GetService<Veda.Storage.CosmosDbInitializer>();
if (cosmosInitializer is not null)
{
    using var initCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
    try
    {
        await cosmosInitializer.EnsureReadyAsync(initCts.Token);
    }
    catch (Exception ex)
    {
        var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
        startupLogger.LogWarning(ex,
            "CosmosDB initialisation failed on startup (type={ExType}, msg={Msg}). " +
            "Check Managed Identity role assignments and CosmosDB endpoint.",
            ex.GetType().Name, ex.Message);
    }
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
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthorization();
app.MapHealthChecks("/health");  // Public, excluded from ApiKeyMiddleware
app.MapControllers();
app.MapGraphQL();   // GraphQL endpoint: /graphql (Banana Cake Pop UI in dev)
app.MapVedaMcp();   // MCP endpoint: /mcp (SSE transport for Copilot Chat)
app.MapFallbackToFile("index.html");  // SPA routing fallback

app.Run();
