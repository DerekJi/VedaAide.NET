namespace Veda.Core.Interfaces;

/// <summary>
/// 可插拔语义增强层接口，确保摄入和检索的语义对齐。
/// 默认实现 NoOpSemanticEnhancer 直接透传；
/// 配置词库文件后自动切换到 PersonalVocabularyEnhancer。
///
/// 设计原则：
/// - GetEnhancedMetadataAsync：摄入时使用，同时应用 Vocabulary（术语+同义词）和 Tags（规则匹配）
/// - ExpandQueryAsync：检索时使用，应用相同的 Vocabulary 扩展逻辑
/// - 这两个方法应该产生对称的结果，确保摄入发现的术语在检索时也能被找到
/// </summary>
public interface ISemanticEnhancer
{
    /// <summary>
    /// 从 chunk 内容生成完整的语义增强元数据（SRP：单一职责 = 语义增强）。
    /// 在摄入时调用，返回结果包含别名标签、检测到的术语、及其同义词。
    /// 这确保摄入时的标注与检索时的扩展逻辑对齐。
    /// </summary>
    Task<SemanticEnhancementResult> GetEnhancedMetadataAsync(string content, CancellationToken ct = default);

    /// <summary>查询扩展：将缩写/自定义词汇映射到规范化同义词，返回扩展后的查询字符串。</summary>
    Task<string> ExpandQueryAsync(string query, CancellationToken ct = default);

    /// <summary>别名注入：为 chunk 内容推导用户自定义别名标签列表（新实现通过 GetEnhancedMetadataAsync）。</summary>
    Task<IReadOnlyList<string>> GetAliasTagsAsync(string content, CancellationToken ct = default);
}
