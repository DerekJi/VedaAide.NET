using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Veda.Core.Interfaces;

namespace Veda.Storage;

/// <summary>
/// Token usage repository — CosmosDB cloud implementation.
/// Stores AI model token consumption records in the TokenUsages container, partitioned by /userId.
/// </summary>
public sealed class CosmosDbTokenUsageRepository(
    CosmosClient client,
    CosmosDbOptions options,
    ILogger<CosmosDbTokenUsageRepository> logger) : ITokenUsageRepository
{
    private Container Container =>
        client.GetDatabase(options.DatabaseName).GetContainer(options.TokenUsagesContainerName);

    public async Task RecordAsync(TokenUsageRecord record, CancellationToken ct = default)
    {
        var doc = new
        {
            id               = Guid.NewGuid().ToString(),
            userId           = record.UserId,
            modelName        = record.ModelName,
            operationType    = record.OperationType,
            promptTokens     = record.PromptTokens,
            completionTokens = record.CompletionTokens,
            totalTokens      = record.PromptTokens + record.CompletionTokens,
            createdAtUtc     = DateTimeOffset.UtcNow
        };
        await Container.CreateItemAsync(doc, new PartitionKey(record.UserId), cancellationToken: ct);
        logger.LogDebug("Recorded {Total} tokens for user {UserId} ({Op})", doc.totalTokens, record.UserId, record.OperationType);
    }

    public async Task<TokenUsageSummary> GetSummaryAsync(string userId, CancellationToken ct = default)
    {
        var queryDef = new QueryDefinition(
            "SELECT c.modelName, c.operationType, c.promptTokens, c.completionTokens, c.totalTokens, c.createdAtUtc " +
            "FROM c WHERE c.userId = @u")
            .WithParameter("@u", userId);

        var rows = new List<TokenUsageDoc>();
        try
        {
            var iter = Container.GetItemQueryIterator<TokenUsageDoc>(queryDef,
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

            while (iter.HasMoreResults)
            {
                var page = await iter.ReadNextAsync(ct);
                rows.AddRange(page);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound
                                                        or System.Net.HttpStatusCode.ServiceUnavailable)
        {
            logger.LogWarning("TokenUsages container unavailable ({Status}) — returning empty usage summary", ex.StatusCode);
            // Return empty summary rather than 500; container will be created by initializer on next startup
        }

        var monthStart = new DateTimeOffset(
            DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);

        return new TokenUsageSummary(
            ThisMonth: BuildPeriod(rows.Where(x => x.createdAtUtc >= monthStart)),
            AllTime:   BuildPeriod(rows));
    }

    private static TokenUsagePeriod BuildPeriod(IEnumerable<TokenUsageDoc> rows)
    {
        var byModel = rows
            .GroupBy(x => (x.modelName, x.operationType))
            .Select(g => new TokenUsageByModel(
                ModelName:        g.Key.modelName,
                OperationType:    g.Key.operationType,
                PromptTokens:     g.Sum(x => x.promptTokens),
                CompletionTokens: g.Sum(x => x.completionTokens),
                TotalTokens:      g.Sum(x => x.totalTokens)))
            .OrderByDescending(x => x.TotalTokens)
            .ToList();

        return new TokenUsagePeriod(
            TotalTokens: byModel.Sum(x => x.TotalTokens),
            ByModel:     byModel);
    }

    private record TokenUsageDoc(
        string modelName,
        string operationType,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        DateTimeOffset createdAtUtc);
}
