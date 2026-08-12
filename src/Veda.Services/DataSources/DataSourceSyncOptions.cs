namespace Veda.Core.Options;

/// <summary>
/// Background Service auto-sync configuration section: <c>Veda:DataSources:AutoSync</c>
/// </summary>
public sealed class DataSourceSyncOptions
{
    /// <summary>Whether background auto-sync is enabled; defaults to false.</summary>
    public bool Enabled         { get; set; } = false;

    /// <summary>Sync interval in minutes; minimum 1, default 60.</summary>
    public int  IntervalMinutes { get; set; } = 60;
}
