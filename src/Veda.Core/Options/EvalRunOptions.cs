namespace Veda.Core.Options;

/// <summary>Configuration options for an evaluation run.</summary>
public record EvalRunOptions
{
    /// <summary>Restricts this run to the given question IDs; an empty array means run the entire Golden Dataset.</summary>
    public string[] QuestionIds { get; init; } = [];

    /// <summary>Overrides the Chat model name in the configuration (for A/B comparison); null means use the default configuration.</summary>
    public string? ChatModelOverride { get; init; }

    /// <summary>Dataset source to load questions from; defaults to the Golden Dataset.</summary>
    public EvalDatasetSource DatasetSource { get; init; } = EvalDatasetSource.Database;

    /// <summary>Per-source dataset configuration (repo id, local path, max records, ...); null means provider defaults.</summary>
    public EvalDatasetConfig? DatasetConfig { get; init; }
}
