namespace Veda.Services;

/// <summary>
/// 单例：跨请求持久化 Azure AI Document Intelligence 配额超限状态。
/// 超限后自动将截止时间设为下个自然月 1 日 00:00 UTC（对齐 Azure DI 免费配额周期）。
/// 线程安全：通过 Interlocked 操作保护 long 字段。
/// </summary>
public sealed class AzureDiQuotaState
{
    // 0 = 未超限；> 0 = 超限截止时间的 UTC Ticks
    private long _quotaExceededUntilTicks;

    /// <summary>当前是否处于配额超限状态。</summary>
    public bool IsExceeded
    {
        get
        {
            var ticks = Interlocked.Read(ref _quotaExceededUntilTicks);
            return ticks > 0 && DateTimeOffset.UtcNow.UtcTicks < ticks;
        }
    }

    /// <summary>标记配额超限，截止时间为下个自然月 1 日 00:00 UTC。</summary>
    public void MarkExceeded()
    {
        var now = DateTimeOffset.UtcNow;
        var nextMonth = now.Month == 12
            ? new DateTimeOffset(now.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(now.Year, now.Month + 1, 1, 0, 0, 0, TimeSpan.Zero);
        Interlocked.Exchange(ref _quotaExceededUntilTicks, nextMonth.UtcTicks);
    }

    /// <summary>仅供测试使用：直接设置截止时间。</summary>
    internal void SetExceededUntilForTest(DateTimeOffset? until) =>
        Interlocked.Exchange(ref _quotaExceededUntilTicks, until?.UtcTicks ?? 0L);
}
