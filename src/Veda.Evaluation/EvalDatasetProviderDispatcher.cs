namespace Veda.Evaluation;

/// <summary>
/// Composite <see cref="IEvalDatasetProvider"/> that routes an <see cref="EvalDatasetSource"/>
/// to the first registered provider whose <see cref="IEvalDatasetProvider.Supports"/> matches.
/// Concrete providers self-register in their own layer (e.g. <c>Veda.Services</c>), so new sources
/// can be added without modifying the runner or this dispatcher.
/// Throws <see cref="NotSupportedException"/> when no registered provider handles the source.
/// </summary>
public sealed class EvalDatasetProviderDispatcher(IServiceProvider services) : IEvalDatasetProvider
{
    /// <summary>
    /// A dispatcher is not a leaf provider — it always reports <see langword="false"/> so that
    /// provider resolution (see <see cref="LoadAsync"/>) cannot recurse into another dispatcher instance.
    /// </summary>
    public bool Supports(EvalDatasetSource source) => false;

    public async Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,
        EvalDatasetConfig? config,
        CancellationToken ct = default)
    {
        // Resolve lazily (not in the constructor): this registration is itself an
        // IEvalDatasetProvider, so touching the container during construction would recurse.
        var providers = services.GetServices<IEvalDatasetProvider>()
            .Where(p => p is not EvalDatasetProviderDispatcher)
            .ToArray();

        var provider = providers.FirstOrDefault(p => p.Supports(source))
            ?? throw new NotSupportedException(
                $"No registered IEvalDatasetProvider supports dataset source '{source}'." +
                (providers.Length == 0
                    ? " No providers are registered — did you call AddVedaAiServices()?"
                    : $" Registered: {string.Join(", ", providers.Select(p => p.GetType().Name))}."));

        return await provider.LoadAsync(source, config, ct);
    }
}
