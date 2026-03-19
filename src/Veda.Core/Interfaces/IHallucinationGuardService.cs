namespace Veda.Core.Interfaces;

/// <summary>
/// 防幻觉第二层：LLM 自我校验服务契约。
/// 通过额外的 LLM 调用逐句审核回答是否完全来源于检索到的上下文。
/// 由 Veda:Rag:EnableSelfCheckGuard 配置项控制是否启用。
/// </summary>
public interface IHallucinationGuardService
{
    /// <summary>
    /// 验证回答是否完全由提供的上下文支撑。
    /// </summary>
    /// <param name="answer">LLM 生成的回答文本。</param>
    /// <param name="context">检索到的原始文档上下文（BuildContext 输出）。</param>
    /// <returns>true 表示回答有文档依据（无幻觉）；false 表示检测到潜在幻觉内容。</returns>
    Task<bool> VerifyAsync(string answer, string context, CancellationToken ct = default);
}
