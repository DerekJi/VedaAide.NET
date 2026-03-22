namespace Veda.Core.Interfaces;

/// <summary>评估报告的持久化与查询接口。</summary>
public interface IEvalReportRepository
{
    Task<IReadOnlyList<EvaluationReport>> ListAsync(int limit = 20, CancellationToken ct = default);
    Task<EvaluationReport?> GetAsync(string runId, CancellationToken ct = default);
    Task SaveAsync(EvaluationReport report, CancellationToken ct = default);
    Task DeleteAsync(string runId, CancellationToken ct = default);
}
