namespace Veda.Core;

/// <summary>
/// 语义增强结果：摄入和检索时的统一扩展信息。
/// 确保摄入时生成的元数据与检索时的扩展逻辑保持一致。
/// </summary>
public sealed record SemanticEnhancementResult
{
    /// <summary>通过 Tags 规则匹配到的别名标签（如 "contract-type", "party-role" 等）。</summary>
    public required IReadOnlyList<string> AliasTags { get; init; }

    /// <summary>通过 Vocabulary 术语匹配到的所有相关术语及其同义词。</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> DetectedTermsWithSynonyms { get; init; }

    /// <summary>完整的扩展文本：原始内容 + 所有检测到的术语和同义词，用于向量化增强。</summary>
    public required string EnrichedContent { get; init; }
}
