namespace Veda.Core.Interfaces;

/// <summary>
/// Token 消耗记录仓储接口。
/// 负责写入单条消耗记录和按用户聚合统计查询。
/// </summary>
public interface ITokenUsageRepository
{
    Task RecordAsync(TokenUsageRecord record, CancellationToken ct = default);

    Task<TokenUsageSummary> GetSummaryAsync(string userId, CancellationToken ct = default);
}

/// <summary>单次 AI 调用的 token 消耗记录（领域值对象）。</summary>
public record TokenUsageRecord(
    string UserId,
    string ModelName,
    string OperationType,   // Chat | Embedding | Vision
    int PromptTokens,
    int CompletionTokens
);

/// <summary>按用户汇总的 token 消耗报告。</summary>
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
