using Veda.Core.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Veda.Services;

/// <summary>
/// Personal vocabulary semantic enhancer backed by a JSON configuration file.
/// The vocabulary file path is configured via Veda:Semantics:VocabularyFilePath.
///
/// Design: at ingestion, GetEnhancedMetadataAsync applies both Vocabulary and Tags;
/// at query time, ExpandQueryAsync applies the same Vocabulary expansion,
/// keeping the semantic enhancement logic symmetric and consistent on both sides.
/// </summary>
public sealed class PersonalVocabularyEnhancer : ISemanticEnhancer
{
    private readonly VocabularyData _vocab;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public PersonalVocabularyEnhancer(SemanticsOptions options)
    {
        _vocab = LoadVocabulary(options.VocabularyFilePath);
    }

    /// <summary>
    /// Generates the complete semantic-enhancement metadata for the ingestion stage (SRP: single responsibility).
    /// 1. Detects Tags rules and generates alias tags;
    /// 2. Detects Vocabulary terms and collects their synonyms;
    /// 3. Replaces each term in place with the "term (synonym1 synonym2)" format (only the first occurrence)
    /// 4. EnrichedContent is used for Embedding generation, ensuring semantic coherence.
    /// </summary>
    public Task<SemanticEnhancementResult> GetEnhancedMetadataAsync(string content, CancellationToken ct = default)
    {
        // 1. Generate alias tags from the Tags rules
        var aliasTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tagRule in _vocab.Tags)
        {
            if (Regex.IsMatch(content, tagRule.Pattern, RegexOptions.IgnoreCase))
                foreach (var label in tagRule.Labels)
                    aliasTags.Add(label);
        }

        // 2. Detect terms and their synonyms from the Vocabulary
        var detectedTerms = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _vocab.Vocabulary)
        {
            if (content.Contains(entry.Term, StringComparison.OrdinalIgnoreCase))
            {
                if (!detectedTerms.TryGetValue(entry.Term, out var synonymSet))
                {
                    synonymSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    detectedTerms[entry.Term] = synonymSet;
                }
                foreach (var syn in entry.Synonyms)
                    synonymSet.Add(syn);
            }
        }

        // 3. Build the "enriched content": replace each term in place with the "term (synonym1 synonym2)" format
        //    preserving the original matched casing, without repeated replacement
        var enrichedContent = content;
        foreach (var (term, synonymSet) in detectedTerms)
        {
            if (synonymSet.Count == 0) continue;

            var synonymsStr = string.Join(" ", synonymSet);
            // Use $& to preserve the original matched casing, e.g. "BG" stays as "BG (..."
            var escapedTerm = Regex.Escape(term);
            var pattern = $@"\b{escapedTerm}\b(?!\s*\()"; // word boundary, not followed by (...)
            enrichedContent = Regex.Replace(enrichedContent, pattern,
                m => $"{m.Value} ({synonymsStr})", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        // 4. Convert to a read-only structure
        var termsWithSynonyms = detectedTerms.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.ToList().AsReadOnly()
        ) as IReadOnlyDictionary<string, IReadOnlyList<string>>
            ?? new Dictionary<string, IReadOnlyList<string>>();

        var result = new SemanticEnhancementResult
        {
            AliasTags = aliasTags.ToList().AsReadOnly(),
            DetectedTermsWithSynonyms = termsWithSynonyms,
            EnrichedContent = enrichedContent
        };

        return Task.FromResult(result);
    }

    public Task<string> ExpandQueryAsync(string query, CancellationToken ct = default)
    {
        if (_vocab.Vocabulary.Count == 0) return Task.FromResult(query);

        var expanded = query;
        foreach (var entry in _vocab.Vocabulary)
        {
            if (expanded.Contains(entry.Term, StringComparison.OrdinalIgnoreCase))
            {
                var synonyms = string.Join(" ", entry.Synonyms);
                expanded = Regex.Replace(expanded, Regex.Escape(entry.Term),
                    $"{entry.Term} {synonyms}", RegexOptions.IgnoreCase);
            }
        }
        return Task.FromResult(expanded.Trim());
    }

    public async Task<IReadOnlyList<string>> GetAliasTagsAsync(string content, CancellationToken ct = default)
    {
        // For backward compatibility, obtain the alias tags via GetEnhancedMetadataAsync
        var enhanced = await GetEnhancedMetadataAsync(content, ct);
        return enhanced.AliasTags;
    }

    private static VocabularyData LoadVocabulary(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new VocabularyData();
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<VocabularyData>(json, JsonOpts) ?? new VocabularyData();
        }
        catch
        {
            return new VocabularyData();
        }
    }

    // ── Internal model ────────────────────────────────────────────────────────

    private sealed class VocabularyData
    {
        [JsonPropertyName("vocabulary")]
        public List<VocabEntry> Vocabulary { get; set; } = [];

        [JsonPropertyName("tags")]
        public List<TagRule> Tags { get; set; } = [];
    }

    private sealed class VocabEntry
    {
        [JsonPropertyName("term")]
        public string Term { get; set; } = string.Empty;

        [JsonPropertyName("synonyms")]
        public List<string> Synonyms { get; set; } = [];
    }

    private sealed class TagRule
    {
        [JsonPropertyName("pattern")]
        public string Pattern { get; set; } = string.Empty;

        [JsonPropertyName("labels")]
        public List<string> Labels { get; set; } = [];
    }
}
