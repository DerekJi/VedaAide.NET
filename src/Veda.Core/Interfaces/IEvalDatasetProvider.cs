namespace Veda.Core.Interfaces;

/// <summary>
/// Loads evaluation questions from a single data source.
/// Each implementation handles one <see cref="EvalDatasetSource"/> (Database / HuggingFace / LocalFile)
/// and declares which one via <see cref="Supports"/>. Callers dispatch through the registered providers
/// (see <c>EvalDatasetProviderDispatcher</c>), so new sources can be added without touching the runner.
/// </summary>
public interface IEvalDatasetProvider
{
    /// <summary>Whether this provider can load questions for <paramref name="source"/>.</summary>
    bool Supports(EvalDatasetSource source);

    /// <summary>
    /// Loads questions for <paramref name="source"/> using <paramref name="config"/>.
    /// Implementations should treat a null <paramref name="config"/> as provider defaults,
    /// and throw <see cref="NotSupportedException"/> if <paramref name="source"/> is not
    /// supported (defensive; normally guarded by <see cref="Supports"/>).
    /// </summary>
    Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,
        EvalDatasetConfig? config,
        CancellationToken ct = default);
}
