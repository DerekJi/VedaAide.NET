namespace Veda.Core.Options;

/// <summary>Configuration options for an evaluation run.</summary>
public record EvalRunOptions
{
    /// <summary>Restricts this run to the given question IDs; an empty array means run the entire Golden Dataset.</summary>
    public string[] QuestionIds { get; init; } = [];

    /// <summary>Overrides the Chat model name in the configuration (for A/B comparison); null means use the default configuration.</summary>
    public string? ChatModelOverride { get; init; }
}
