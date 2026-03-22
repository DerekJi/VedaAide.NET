using Microsoft.EntityFrameworkCore;
using Veda.Agents;
using Veda.Api.GraphQL;
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

// ── AI Services (Ollama via Semantic Kernel) ──────────────────────────────────
builder.Services.AddVedaAiServices(
    ollamaEndpoint: cfg["Veda:OllamaEndpoint"] ?? "http://localhost:11434",
    embeddingModel: cfg["Veda:EmbeddingModel"] ?? "nomic-embed-text",
    chatModel: cfg["Veda:ChatModel"] ?? "qwen3:8b");

// ── RAG Options (thresholds / feature flags) ──────────────────────────────────
builder.Services.Configure<RagOptions>(cfg.GetSection("Veda:Rag"));
builder.Services.Configure<VedaOptions>(cfg.GetSection("Veda"));

// ── Storage (SQLite + EF Core) ────────────────────────────────────────────────
builder.Services.AddVedaStorage(cfg["Veda:DbPath"] ?? "veda.db");

// ── Prompts (Context Window Builder) ─────────────────────────────────────────
builder.Services.AddVedaPrompts();

// ── Agents (Orchestration) ────────────────────────────────────────────────────
builder.Services.AddVedaAgents();
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
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "VedaAide API", Version = "v1" });
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

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.MapGraphQL();   // GraphQL endpoint: /graphql (Banana Cake Pop UI in dev)
app.MapVedaMcp();   // MCP endpoint: /mcp (SSE transport for Copilot Chat)

app.Run();
