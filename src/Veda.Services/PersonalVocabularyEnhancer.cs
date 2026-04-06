using Veda.Core.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Veda.Services;

/// <summary>
/// 基于 JSON 配置文件的个人词库语义增强实现。
/// 词库文件路径通过 Veda:Semantics:VocabularyFilePath 配置。
///
/// 设计：确保摄入时使用 GetEnhancedMetadataAsync 同时应用 Vocabulary 和 Tags，
/// 检索时使用 ExpandQueryAsync 应用相同的 Vocabulary 扩展，
/// 使得两侧的语义增强逻辑对称且一致。
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
    /// 为摄入阶段生成完整的语义增强元数据（SRP：单一职责）。
    /// 1. 检测 Tags 规则，生成别名标签；
    /// 2. 检测 Vocabulary 术语，收集同义词；
    /// 3. 将术语就地替换为 "term (synonym1 synonym2)" 格式（仅替换首次出现）
    /// 4. EnrichedContent 用于 Embedding 生成，保证语义连贯性。
    /// </summary>
    public Task<SemanticEnhancementResult> GetEnhancedMetadataAsync(string content, CancellationToken ct = default)
    {
        // 1. 从 Tags 规则生成别名标签
        var aliasTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tagRule in _vocab.Tags)
        {
            if (Regex.IsMatch(content, tagRule.Pattern, RegexOptions.IgnoreCase))
                foreach (var label in tagRule.Labels)
                    aliasTags.Add(label);
        }

        // 2. 从 Vocabulary 检测术语及其同义词
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

        // 3. 构建"增强内容"：就地替换术语为 "term (synonym1 synonym2)" 格式
        // 保留原始匹配的大小写，不重复替换
        var enrichedContent = content;
        foreach (var (term, synonymSet) in detectedTerms)
        {
            if (synonymSet.Count == 0) continue;

            var synonymsStr = string.Join(" ", synonymSet);
            // 使用 $& 保留原始匹配的大小写，例如 "BG" 保持为 "BG (..."
            var escapedTerm = Regex.Escape(term);
            var pattern = $@"\b{escapedTerm}\b(?!\s*\()"; // word boundary，后面不跟 (...)
            enrichedContent = Regex.Replace(enrichedContent, pattern,
                m => $"{m.Value} ({synonymsStr})", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        // 4. 转换为只读结构
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
        // 为了向后兼容，通过 GetEnhancedMetadataAsync 的结果获取别名标签
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
