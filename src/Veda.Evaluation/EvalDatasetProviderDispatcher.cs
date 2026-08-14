namespace Veda.Evaluation;

/// <summary>
/// Routes an <see cref="EvalDatasetSource"/> to the first registered <see cref="IEvalDatasetProvider"/>
/// whose <see cref="IEvalDatasetProvider.Supports"/> matches. Leaf providers are constructor-injected as
/// an <see cref="IEnumerable{T}"/> (they self-register via <c>TryAddEnumerable</c> in their own layer), so
/// routing is independent of DI registration order and there is no service-locator resolution.
/// Throws <see cref="UnsupportedEvalDatasetSourceException"/> when no registered provider handles the source.
/// </summary>
public sealed class EvalDatasetProviderDispatcher(IEnumerable<IEvalDatasetProvider> providers)
    : IEvalDatasetSourceRouter
{
    public async Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,
        EvalDatasetConfig? config,
        CancellationToken ct = default)
    {
        var providerList = providers.ToArray();

        var provider = providerList.FirstOrDefault(p => p.Supports(source))
            ?? throw new UnsupportedEvalDatasetSourceException(source, noProvidersRegistered: providerList.Length == 0);

        return await provider.LoadAsync(source, config, ct);
    }
}
