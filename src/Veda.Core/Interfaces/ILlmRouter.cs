namespace Veda.Core.Interfaces;

/// <summary>
/// LLM 路由器：根据 QueryMode 返回对应的 IChatService 实现。
/// Simple → 轻量模型（Ollama / GPT-4o-mini）
/// Advanced → 重量级模型（Ollama / DeepSeek）
/// 配置缺失时（如 DeepSeek ApiKey 未填写），Advanced 自动降级到 Simple。
/// </summary>
public interface ILlmRouter
{
    IChatService Resolve(QueryMode mode);
}
