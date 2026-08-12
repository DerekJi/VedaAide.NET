namespace Veda.Core.Interfaces;

/// <summary>
/// Interface for the user-level private memory layer.
/// Records behavior events and provides retrieval weight preferences based on historical feedback.
/// </summary>
public interface IUserMemoryStore
{
    /// <summary>Records a behavior event (async, does not block the main flow).</summary>
    Task RecordEventAsync(UserBehaviorEvent evt, CancellationToken ct = default);

    /// <summary>
    /// Gets the weight boost factor for a specific chunk for the user.
    /// Returns 1.0 (does not affect ranking) when there is no history; > 1.0 after positive feedback, < 1.0 after negative feedback.
    /// </summary>
    Task<float> GetBoostFactorAsync(string userId, string chunkId, CancellationToken ct = default);

    /// <summary>Gets the user's personalized term preferences (terms that appear frequently in positive feedback).</summary>
    Task<IReadOnlyDictionary<string, float>> GetTermPreferencesAsync(
        string userId, CancellationToken ct = default);

    /// <summary>Returns feedback statistics (for the admin stats endpoint).</summary>
    Task<FeedbackStats> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>Summary of feedback statistics.</summary>
public record FeedbackStats(
    int TotalEvents,
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<RejectedChunkInfo> TopRejectedChunks);

/// <summary>Information about chunks frequently marked as irrelevant.</summary>
public record RejectedChunkInfo(
    string ChunkId,
    string? DocumentName,
    int RejectionCount);
