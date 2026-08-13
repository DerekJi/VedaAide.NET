namespace Veda.Core.Interfaces;

/// <summary>
/// Loads evaluation questions from a data source.
/// Implementations are responsible for a single <see cref="EvalDatasetSource"/>
/// (Database / HuggingFace / LocalFile) and throw <see cref="NotSupportedException"/>
/// for sources they do not handle, so new sources can be added without touching the runner.
/// </summary>
public interface IEvalDatasetProvider
{
    Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,
        EvalDatasetConfig config,
        CancellationToken ct = default);
}
