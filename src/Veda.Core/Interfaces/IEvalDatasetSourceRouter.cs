namespace Veda.Core.Interfaces;

/// <summary>
/// Routes an <see cref="EvalDatasetSource"/> to the <see cref="IEvalDatasetProvider"/> that supports it.
/// The evaluation runner depends on this router (rather than on a concrete provider) so that which provider
/// a run receives never depends on DI registration order. Leaf providers self-register via
/// <c>TryAddEnumerable</c> and the router consumes them as an <see cref="IEnumerable{T}"/>.
/// </summary>
public interface IEvalDatasetSourceRouter
{
    /// <summary>
    /// Loads questions for <paramref name="source"/> using <paramref name="config"/>, dispatching to the
    /// first registered provider whose <see cref="IEvalDatasetProvider.Supports"/> matches.
    /// Throws <see cref="UnsupportedEvalDatasetSourceException"/> when no provider handles the source.
    /// </summary>
    Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,
        EvalDatasetConfig? config,
        CancellationToken ct = default);
}
