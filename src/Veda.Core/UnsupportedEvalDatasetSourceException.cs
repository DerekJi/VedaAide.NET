namespace Veda.Core;

/// <summary>
/// Thrown when no <see cref="Interfaces.IEvalDatasetProvider"/> is registered for the requested
/// <see cref="EvalDatasetSource"/>. Derives from <see cref="NotSupportedException"/>; its message is
/// safe to surface to API clients (it contains no internal type names, paths, or DI setup details).
/// </summary>
public sealed class UnsupportedEvalDatasetSourceException : NotSupportedException
{
    /// <summary>The source that no provider supports.</summary>
    public EvalDatasetSource DatasetSource { get; }

    /// <summary>
    /// Creates the exception. Set <paramref name="noProvidersRegistered"/> when the container has no
    /// dataset providers at all (a server misconfiguration) rather than a source that is merely unsupported.
    /// </summary>
    public UnsupportedEvalDatasetSourceException(EvalDatasetSource source, bool noProvidersRegistered = false)
        : base(noProvidersRegistered
            ? $"No evaluation dataset providers are registered."
            : $"No evaluation dataset provider supports source '{source}'.")
    {
        DatasetSource = source;
    }
}
