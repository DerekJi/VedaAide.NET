namespace Veda.Services;

/// <summary>
/// 语义增强默认透传实现——不修改查询和内容。
/// 当未配置词库文件时由 DI 注入此实现。
/// </summary>
public sealed class NoOpSemanticEnhancer : ISemanticEnhancer
{
    public Task<string> ExpandQueryAsync(string query, CancellationToken ct = default)
        => Task.FromResult(query);

    public Task<IReadOnlyList<string>> GetAliasTagsAsync(string content, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
