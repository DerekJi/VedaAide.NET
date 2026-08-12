namespace Veda.Core.Interfaces;

/// <summary>
/// Pluggable semantic enhancement layer interface, ensuring semantic alignment between ingestion and retrieval.
/// The default implementation, NoOpSemanticEnhancer, passes content through unchanged;
/// once a vocabulary file is configured, it automatically switches to PersonalVocabularyEnhancer.
///
/// Design principles:
/// - GetEnhancedMetadataAsync: used at ingestion time, applying both Vocabulary (terms + synonyms) and Tags (rule-based matching)
/// - ExpandQueryAsync: used at retrieval time, applying the same Vocabulary expansion logic
/// - These two methods should produce symmetric results, ensuring that terms discovered at ingestion are also found at retrieval
/// </summary>
public interface ISemanticEnhancer
{
    /// <summary>
    /// Generates complete semantic enhancement metadata from chunk content (SRP: single responsibility = semantic enhancement).
    /// Called at ingestion time; the result contains alias tags, detected terms, and their synonyms.
    /// This ensures that annotation at ingestion stays aligned with expansion logic at retrieval.
    /// </summary>
    Task<SemanticEnhancementResult> GetEnhancedMetadataAsync(string content, CancellationToken ct = default);

    /// <summary>Query expansion: maps abbreviations / custom terms to normalized synonyms and returns the expanded query string.</summary>
    Task<string> ExpandQueryAsync(string query, CancellationToken ct = default);

    /// <summary>Alias injection: derives a list of user-defined alias tags for chunk content (newer implementations use GetEnhancedMetadataAsync).</summary>
    Task<IReadOnlyList<string>> GetAliasTagsAsync(string content, CancellationToken ct = default);
}
