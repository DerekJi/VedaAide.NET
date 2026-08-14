namespace Veda.Core;

/// <summary>
/// Flexible per-run configuration for loading an evaluation dataset.
/// Only the fields relevant to the selected <see cref="EvalDatasetSource"/> are consumed by the provider.
/// </summary>
public record EvalDatasetConfig
{
    /// <summary>HuggingFace repo id, e.g. "ragas-v1/code-generated" (used by <see cref="EvalDatasetSource.HuggingFace"/>).</summary>
    public string? RepoId { get; init; }

    /// <summary>Path to a local dataset file, e.g. "data/eval-datasets/ragas.json" (used by <see cref="EvalDatasetSource.LocalFile"/>).</summary>
    public string? LocalPath { get; init; }

    /// <summary>Dataset split to load, e.g. "train" / "test" / "validation".</summary>
    public string? Split { get; init; }

    /// <summary>Maximum number of records to load; null means no limit. Must be non-negative (guarded by the provider).</summary>
    public int? MaxRecords { get; init; }

    /// <summary>Prefer a locally cached copy over re-downloading (used by <see cref="EvalDatasetSource.HuggingFace"/>).</summary>
    public bool PreferCache { get; init; } = true;
}
