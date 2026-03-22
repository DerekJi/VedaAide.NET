using System.Text.Json;
using Veda.Storage.Entities;

namespace Veda.Storage;

public sealed class EvalReportRepository(VedaDbContext db) : IEvalReportRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<IReadOnlyList<EvaluationReport>> ListAsync(int limit = 20, CancellationToken ct = default)
    {
        var entities = await db.EvalRuns
            .OrderByDescending(e => e.RunAtTicks)
            .Take(limit)
            .ToListAsync(ct);
        return entities.Select(Deserialize).ToList();
    }

    public async Task<EvaluationReport?> GetAsync(string runId, CancellationToken ct = default)
    {
        var entity = await db.EvalRuns.FindAsync([runId], ct);
        return entity is null ? null : Deserialize(entity);
    }

    public async Task SaveAsync(EvaluationReport report, CancellationToken ct = default)
    {
        var existing = await db.EvalRuns.FindAsync([report.RunId], ct);
        if (existing is null)
        {
            db.EvalRuns.Add(new EvalRunEntity
            {
                RunId      = report.RunId,
                RunAtTicks = report.RunAt.UtcTicks,
                ModelName  = report.ModelName,
                ReportJson = Serialize(report),
            });
        }
        else
        {
            existing.ReportJson = Serialize(report);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string runId, CancellationToken ct = default)
    {
        var entity = await db.EvalRuns.FindAsync([runId], ct);
        if (entity is not null)
        {
            db.EvalRuns.Remove(entity);
            await db.SaveChangesAsync(ct);
        }
    }

    private static string Serialize(EvaluationReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    private static EvaluationReport Deserialize(EvalRunEntity entity) =>
        JsonSerializer.Deserialize<EvaluationReport>(entity.ReportJson, JsonOptions)
        ?? new EvaluationReport { RunId = entity.RunId };
}
