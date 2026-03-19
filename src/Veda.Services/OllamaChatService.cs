using Microsoft.SemanticKernel.ChatCompletion;

namespace Veda.Services;

/// <summary>
/// DIP 适配器：将 SK 框架的 IChatCompletionService 包装为领域接口 IChatService。
/// 领域层（QueryService）只依赖 IChatService，不感知 Semantic Kernel。
/// </summary>
public sealed class OllamaChatService(IChatCompletionService inner) : IChatService
{
    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var history = new ChatHistory(systemPrompt);
        history.AddUserMessage(userMessage);
        var response = await inner.GetChatMessageContentAsync(history, cancellationToken: ct);
        return response.Content ?? string.Empty;
    }
}
