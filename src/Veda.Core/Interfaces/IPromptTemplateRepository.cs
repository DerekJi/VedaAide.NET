namespace Veda.Core.Interfaces;

/// <summary>
/// Repository interface for persisting prompt templates. The concrete implementation lives in Veda.Storage (EF Core).
/// </summary>
public interface IPromptTemplateRepository
{
    Task<PromptTemplate?> GetLatestAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<PromptTemplate>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(PromptTemplate template, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
