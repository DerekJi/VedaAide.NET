namespace Veda.Core;

/// <summary>
/// Semantic enhancement result: unified enrichment information for both ingestion and retrieval.
/// Ensures the metadata generated during ingestion stays consistent with the enrichment logic at retrieval time.
/// </summary>
public sealed record SemanticEnhancementResult
{
    /// <summary>Alias tags matched via the Tags rules (e.g. "contract-type", "party-role", etc.).</summary>
    public required IReadOnlyList<string> AliasTags { get; init; }

    /// <summary>All relevant terms matched via the Vocabulary, together with their synonyms.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> DetectedTermsWithSynonyms { get; init; }

    /// <summary>The full enriched text: original content plus all detected terms and synonyms, used for embedding enrichment.</summary>
    public required string EnrichedContent { get; init; }
}
