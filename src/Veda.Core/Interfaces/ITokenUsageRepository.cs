namespace Veda.Core.Interfaces;

/// <summary>
/// Repository interface for token usage records.
/// Responsible for writing individual usage records and querying per-user aggregated statistics.
/// </summary>
public interface ITokenUsageRepository
{
    Task RecordAsync(TokenUsageRecord record, CancellationToken ct = default);

    Task<TokenUsageSummary> GetSummaryAsync(string userId, CancellationToken ct = default);
}

/// <summary>Token usage record for a single AI call (domain value object).</summary>
public record TokenUsageRecord(
    string UserId,
    string ModelName,
    string OperationType,   // Chat | Embedding | Vision
    int PromptTokens,
    int CompletionTokens
);

/// <summary>Per-user aggregated token usage report.</summary>
public record TokenUsageSummary(
    TokenUsagePeriod ThisMonth,
    TokenUsagePeriod AllTime
);

public record TokenUsagePeriod(
    int TotalTokens,
    IReadOnlyList<TokenUsageByModel> ByModel
);

public record TokenUsageByModel(
    string ModelName,
    string OperationType,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens
);
