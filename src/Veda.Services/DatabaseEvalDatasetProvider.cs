namespace Veda.Services;

/// <summary>
/// <see cref="IEvalDatasetProvider"/> backed by the Golden Dataset repository (the existing DB logic).
/// Only supports <see cref="EvalDatasetSource.Database"/>; other sources throw <see cref="NotSupportedException"/>
/// until dedicated providers (HuggingFace / LocalFile) are added.
/// </summary>
public sealed class DatabaseEvalDatasetProvider(IEvalDatasetRepository datasetRepo) : IEvalDatasetProvider
{
    public async Task<IReadOnlyList<EvalQuestion>> LoadAsync(
        EvalDatasetSource source,
        EvalDatasetConfig config,
        CancellationToken ct = default)
    {
        if (source != EvalDatasetSource.Database)
        {
            throw new NotSupportedException(
                $"{nameof(DatabaseEvalDatasetProvider)} only supports {EvalDatasetSource.Database}; got {source}.");
        }

        var questions = await datasetRepo.ListAsync(ct);

        return config.MaxRecords is { } max && questions.Count > max
            ? questions.Take(max).ToList()
            : questions;
    }
}
