namespace Veda.Core.Interfaces;

/// <summary>
/// 公开简历定制服务契约：根据 JD 检索公开简历素材，流式生成 Markdown 简历。
/// 不依赖当前用户身份，仅检索 Visibility=Public 的文档。
/// </summary>
public interface IPublicResumeTailoringService
{
    /// <summary>
    /// 根据 Job Description 流式生成 Markdown 格式的定制简历。
    /// 每次 yield 返回一个 LLM token 字符串片段。
    /// </summary>
    /// <param name="jobDescription">招聘方提供的岗位描述，最大 4000 字符。</param>
    /// <param name="topK">向量检索返回的最大简历片段数。</param>
    /// <param name="ct">取消令牌。</param>
    IAsyncEnumerable<string> TailorStreamAsync(string jobDescription, int topK, CancellationToken ct = default);
}
