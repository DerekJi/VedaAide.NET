namespace Veda.Services;

/// <summary>
/// Singleton: persists the Azure AI Document Intelligence quota-exceeded state across requests.
/// When exceeded, the deadline is automatically set to the 1st of the next calendar month at 00:00 UTC
/// (aligned with the Azure DI free quota period).
/// Thread-safe: the long field is guarded by Interlocked operations.
/// </summary>
public sealed class AzureDiQuotaState
{
    // 0 = not exceeded; > 0 = UTC ticks of the quota-exceeded deadline
    private long _quotaExceededUntilTicks;

    /// <summary>Whether the quota is currently exceeded.</summary>
    public bool IsExceeded
    {
        get
        {
            var ticks = Interlocked.Read(ref _quotaExceededUntilTicks);
            return ticks > 0 && DateTimeOffset.UtcNow.UtcTicks < ticks;
        }
    }

    /// <summary>Marks the quota as exceeded with a deadline of the 1st of the next calendar month at 00:00 UTC.</summary>
    public void MarkExceeded()
    {
        var now = DateTimeOffset.UtcNow;
        var nextMonth = now.Month == 12
            ? new DateTimeOffset(now.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(now.Year, now.Month + 1, 1, 0, 0, 0, TimeSpan.Zero);
        Interlocked.Exchange(ref _quotaExceededUntilTicks, nextMonth.UtcTicks);
    }

    /// <summary>For tests only: directly sets the deadline.</summary>
    internal void SetExceededUntilForTest(DateTimeOffset? until) =>
        Interlocked.Exchange(ref _quotaExceededUntilTicks, until?.UtcTicks ?? 0L);
}
