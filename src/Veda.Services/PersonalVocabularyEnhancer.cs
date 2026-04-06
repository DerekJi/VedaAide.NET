using Veda.Core.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Veda.Services;

/// <summary>
/// 基于 JSON 配置文件的个人词库语义增强实现。
/// 词库文件路径通过 Veda:Semantics:VocabularyFilePath 配置。
/// </summary>
public sealed class PersonalVocabularyEnhancer : ISemanticEnhancer
{
    private readonly VocabularyData _vocab;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public PersonalVocabularyEnhancer(SemanticsOptions options)
    {
        _vocab = LoadVocabulary(options.VocabularyFilePath);
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

    public Task<IReadOnlyList<string>> GetAliasTagsAsync(string content, CancellationToken ct = default)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tagRule in _vocab.Tags)
        {
            if (Regex.IsMatch(content, tagRule.Pattern, RegexOptions.IgnoreCase))
                foreach (var label in tagRule.Labels)
                    tags.Add(label);
        }
        return Task.FromResult<IReadOnlyList<string>>(tags.ToList());
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
