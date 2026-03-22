namespace Veda.Services.DataSources;

/// <summary>
/// Background Service 自动同步配置节：<c>Veda:DataSources:AutoSync</c>
/// </summary>
public sealed class DataSourceSyncOptions
{
    /// <summary>是否启用后台自动同步，默认 false。</summary>
    public bool Enabled         { get; set; } = false;

    /// <summary>同步间隔（分钟），最小 1，默认 60。</summary>
    public int  IntervalMinutes { get; set; } = 60;
}
