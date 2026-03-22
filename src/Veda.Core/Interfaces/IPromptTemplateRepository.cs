namespace Veda.Core.Interfaces;

/// <summary>
/// Prompt 模板持久化仓储接口。具体实现在 Veda.Storage（EF Core）。
/// </summary>
public interface IPromptTemplateRepository
{
    Task<PromptTemplate?> GetLatestAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<PromptTemplate>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(PromptTemplate template, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
