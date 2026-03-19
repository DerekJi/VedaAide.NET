using Microsoft.EntityFrameworkCore;
using Veda.Services;
using Veda.Storage;

var builder = WebApplication.CreateBuilder(args);
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

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "VedaAide API", Version = "v1" });
});

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

app.Run();
