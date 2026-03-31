using Veda.Core.Interfaces;
using Veda.Storage.Entities;

namespace Veda.Storage;

/// <summary>
/// SQLite-backed token 消耗记录仓储。
/// </summary>
public sealed class TokenUsageRepository(VedaDbContext db) : ITokenUsageRepository
{
    public async Task RecordAsync(TokenUsageRecord record, CancellationToken ct = default)
    {
        db.TokenUsages.Add(new TokenUsageEntity
        {
            UserId           = record.UserId,
            ModelName        = record.ModelName,
            OperationType    = record.OperationType,
            PromptTokens     = record.PromptTokens,
            CompletionTokens = record.CompletionTokens,
            TotalTokens      = record.PromptTokens + record.CompletionTokens,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<TokenUsageSummary> GetSummaryAsync(string userId, CancellationToken ct = default)
    {
        var monthStart = new DateTimeOffset(
            DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1,
            0, 0, 0, TimeSpan.Zero);

        var allRows = await db.TokenUsages
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);

        var thisMonthRows = allRows.Where(x => x.CreatedAtUtc >= monthStart).ToList();

        return new TokenUsageSummary(
            ThisMonth: BuildPeriod(thisMonthRows),
            AllTime:   BuildPeriod(allRows));
    }

    private static TokenUsagePeriod BuildPeriod(IEnumerable<TokenUsageEntity> rows)
    {
        var byModel = rows
            .GroupBy(x => (x.ModelName, x.OperationType))
            .Select(g => new TokenUsageByModel(
                ModelName:        g.Key.ModelName,
                OperationType:    g.Key.OperationType,
                PromptTokens:     g.Sum(x => x.PromptTokens),
                CompletionTokens: g.Sum(x => x.CompletionTokens),
                TotalTokens:      g.Sum(x => x.TotalTokens)))
            .OrderByDescending(x => x.TotalTokens)
            .ToList();

        return new TokenUsagePeriod(
            TotalTokens: byModel.Sum(x => x.TotalTokens),
            ByModel:     byModel);
    }
}
