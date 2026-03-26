namespace Veda.Core.Interfaces;

/// <summary>
/// 可插拔语义增强层接口。
/// 默认实现 NoOpSemanticEnhancer 直接透传；
/// 配置词库文件后自动切换到 PersonalVocabularyEnhancer。
/// </summary>
public interface ISemanticEnhancer
{
    /// <summary>查询扩展：将缩写/自定义词汇映射到规范化同义词，返回扩展后的查询字符串。</summary>
    Task<string> ExpandQueryAsync(string query, CancellationToken ct = default);

    /// <summary>别名注入：为 chunk 内容推导用户自定义别名标签列表。</summary>
    Task<IReadOnlyList<string>> GetAliasTagsAsync(string content, CancellationToken ct = default);
}
