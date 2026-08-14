namespace Veda.Services;

/// <summary>
/// <see cref="IEvalDatasetProvider"/> backed by the Golden Dataset repository (the existing DB logic).
/// Handles <see cref="EvalDatasetSource.Database"/> only; other sources are reported as unsupported
/// via <see cref="Supports"/> and rejected defensively by <see cref="LoadAsync"/> until dedicated
/// providers (HuggingFace / LocalFile) are added.
/// </summary>
public sealed class DatabaseEvalDatasetProvider(IEvalDatasetRepository datasetRepo) : IEvalDatasetProvider
{
    public bool Supports(EvalDatasetSource source) => source == EvalDatasetSource.Database;

    public async Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,
        EvalDatasetConfig? config,
        CancellationToken ct = default)
    {
        if (!Supports(source))
        {
            throw new NotSupportedException(
                $"{nameof(DatabaseEvalDatasetProvider)} only supports {EvalDatasetSource.Database}; got {source}.");
        }

        config ??= new EvalDatasetConfig();
        if (config.MaxRecords is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.MaxRecords), config.MaxRecords,
                "MaxRecords must be >= 0.");
        }

        var questions = await datasetRepo.ListAsync(ct);

        return config.MaxRecords is { } max && questions.Count > max
            ? questions.Take(max).ToList()
            : questions;
    }
}
