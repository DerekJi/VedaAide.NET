namespace Veda.Services;

/// <summary>
/// Default pass-through semantic enhancer — does not modify the query or content.
/// Injected by DI when no vocabulary file is configured.
/// </summary>
public sealed class NoOpSemanticEnhancer : ISemanticEnhancer
{
    public Task<SemanticEnhancementResult> GetEnhancedMetadataAsync(string content, CancellationToken ct = default)
    {
        var result = new SemanticEnhancementResult
        {
            AliasTags = [],
            DetectedTermsWithSynonyms = new Dictionary<string, IReadOnlyList<string>>(),
            EnrichedContent = content
        };
        return Task.FromResult(result);
    }

    public Task<string> ExpandQueryAsync(string query, CancellationToken ct = default)
        => Task.FromResult(query);

    public Task<IReadOnlyList<string>> GetAliasTagsAsync(string content, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
