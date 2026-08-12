using Microsoft.Extensions.Logging;
using Veda.Core.Interfaces;

namespace Veda.Services;

/// <summary>
/// Legacy adapter: kept for backward compatibility.
/// New code should use AiChatService with IChatClient instead.
/// Wraps a generic IChatService as the domain interface IChatService.
/// </summary>
[Obsolete("Use AiChatService with IChatClient instead", false)]
public sealed class OllamaChatService : IChatService
{
    private readonly IChatService _inner;

    public OllamaChatService(IChatService inner)
    {
        _inner = inner;
    }

    public Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
        => _inner.CompleteAsync(systemPrompt, userMessage, ct);

    public IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt,
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        => _inner.CompleteStreamAsync(systemPrompt, userMessage, ct);
}
