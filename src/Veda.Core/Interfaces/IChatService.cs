namespace Veda.Core.Interfaces;

/// <summary>
/// 对话式 LLM 完成服务契约。
/// DIP：领域层只依赖此接口，不依赖 Semantic Kernel / Ollama 等框架类型。
/// </summary>
public interface IChatService
{
    /// <param name="systemPrompt">系统指令，定义模型行为。</param>
    /// <param name="userMessage">用户消息（含检索到的上下文）。</param>
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);

    /// <summary>
    /// 流式完成：逐 token 返回，适合 Server-Sent Events 场景。
    /// </summary>
    IAsyncEnumerable<string> CompleteStreamAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}
