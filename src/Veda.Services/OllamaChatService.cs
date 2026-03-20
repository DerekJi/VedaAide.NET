using Microsoft.SemanticKernel.ChatCompletion;
using System.Runtime.CompilerServices;

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

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var history = new ChatHistory(systemPrompt);
        history.AddUserMessage(userMessage);
        await foreach (var chunk in inner.GetStreamingChatMessageContentsAsync(history, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
                yield return chunk.Content;
        }
    }
}
