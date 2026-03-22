using System.Text.Json;
using Veda.Storage.Entities;

namespace Veda.Storage;

public sealed class EvalDatasetRepository(VedaDbContext db) : IEvalDatasetRepository
{
    public async Task<IReadOnlyList<EvalQuestion>> ListAsync(CancellationToken ct = default)
    {
        var entities = await db.EvalQuestions
            .OrderByDescending(e => e.CreatedAtTicks)
            .ToListAsync(ct);
        return entities.Select(ToModel).ToList();
    }

    public async Task<EvalQuestion?> GetAsync(string id, CancellationToken ct = default)
    {
        var entity = await db.EvalQuestions.FindAsync([id], ct);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<EvalQuestion> SaveAsync(EvalQuestion question, CancellationToken ct = default)
    {
        var existing = await db.EvalQuestions.FindAsync([question.Id], ct);
        if (existing is null)
        {
            var newEntity = ToEntity(question);
            db.EvalQuestions.Add(newEntity);
        }
        else
        {
            existing.Question       = question.Question;
            existing.ExpectedAnswer = question.ExpectedAnswer;
            existing.TagsJson       = JsonSerializer.Serialize(question.Tags);
        }

        await db.SaveChangesAsync(ct);
        return question;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await db.EvalQuestions.FindAsync([id], ct);
        if (entity is not null)
        {
            db.EvalQuestions.Remove(entity);
            await db.SaveChangesAsync(ct);
        }
    }

    private static EvalQuestion ToModel(EvalQuestionEntity e) => new()
    {
        Id             = e.Id,
        Question       = e.Question,
        ExpectedAnswer = e.ExpectedAnswer,
        Tags           = JsonSerializer.Deserialize<string[]>(e.TagsJson) ?? [],
        CreatedAt      = new DateTimeOffset(e.CreatedAtTicks, TimeSpan.Zero),
    };

    private static EvalQuestionEntity ToEntity(EvalQuestion q) => new()
    {
        Id             = q.Id,
        Question       = q.Question,
        ExpectedAnswer = q.ExpectedAnswer,
        TagsJson       = JsonSerializer.Serialize(q.Tags),
        CreatedAtTicks = q.CreatedAt.UtcTicks,
    };
}
