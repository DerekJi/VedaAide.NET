namespace Veda.Core.Interfaces;

/// <summary>CRUD repository interface for the Golden Dataset.</summary>
public interface IEvalDatasetRepository
{
    Task<IReadOnlyList<EvalQuestion>> ListAsync(CancellationToken ct = default);
    Task<EvalQuestion?> GetAsync(string id, CancellationToken ct = default);
    Task<EvalQuestion> SaveAsync(EvalQuestion question, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
