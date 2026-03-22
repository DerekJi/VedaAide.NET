using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// EF Core 实现的 Prompt 模板仓储。
/// </summary>
public sealed class PromptTemplateRepository(VedaDbContext db) : IPromptTemplateRepository
{
    public async Task<PromptTemplate?> GetLatestAsync(string name, CancellationToken ct = default)
    {
        var entity = await db.PromptTemplates
            .Where(x => x.Name == name)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<PromptTemplate>> ListAsync(CancellationToken ct = default)
    {
        var entities = await db.PromptTemplates
            .OrderBy(x => x.Name)
            .ThenByDescending(x => x.Version)
            .ToListAsync(ct);

        return entities.Select(ToModel).ToList().AsReadOnly();
    }

    public async Task SaveAsync(PromptTemplate template, CancellationToken ct = default)
    {
        var existing = await db.PromptTemplates
            .FirstOrDefaultAsync(x => x.Name == template.Name && x.Version == template.Version, ct);

        if (existing is null)
        {
            db.PromptTemplates.Add(ToEntity(template));
        }
        else
        {
            existing.Content = template.Content;
            existing.DocumentType = (int?)template.DocumentType;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.PromptTemplates.FindAsync([id], ct);
        if (entity is not null)
        {
            db.PromptTemplates.Remove(entity);
            await db.SaveChangesAsync(ct);
        }
    }

    private static PromptTemplate ToModel(PromptTemplateEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Version = e.Version,
        Content = e.Content,
        DocumentType = e.DocumentType.HasValue ? (DocumentType)e.DocumentType.Value : null,
        CreatedAt = new DateTimeOffset(e.CreatedAtTicks, TimeSpan.Zero)
    };

    private static PromptTemplateEntity ToEntity(PromptTemplate t) => new()
    {
        Name = t.Name,
        Version = t.Version,
        Content = t.Content,
        DocumentType = (int?)t.DocumentType,
        CreatedAtTicks = t.CreatedAt.UtcTicks
    };
}
