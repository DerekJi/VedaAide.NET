namespace Veda.Core.Interfaces;

/// <summary>Persistence and query interface for evaluation reports.</summary>
public interface IEvalReportRepository
{
    Task<IReadOnlyList<EvaluationReport>> ListAsync(int limit = 20, CancellationToken ct = default);
    Task<EvaluationReport?> GetAsync(string runId, CancellationToken ct = default);
    Task SaveAsync(EvaluationReport report, CancellationToken ct = default);
    Task DeleteAsync(string runId, CancellationToken ct = default);
}
