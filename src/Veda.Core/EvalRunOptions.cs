namespace Veda.Core;

/// <summary>评估运行的配置选项。</summary>
public record EvalRunOptions
{
    /// <summary>限制本次运行的问题 ID 列表；空数组表示运行全部 Golden Dataset。</summary>
    public string[] QuestionIds { get; init; } = [];

    /// <summary>覆盖配置中的 Chat 模型名称（用于 A/B 对比）；null 表示使用默认配置。</summary>
    public string? ChatModelOverride { get; init; }
}
