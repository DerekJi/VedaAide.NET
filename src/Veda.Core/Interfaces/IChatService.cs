namespace Veda.Core.Interfaces;

/// <summary>
/// Contract for conversational LLM completion services.
/// DIP: the domain layer depends only on this interface, not on framework types such as Semantic Kernel / Ollama.
/// </summary>
public interface IChatService
{
    /// <param name="systemPrompt">System instructions that define the model's behavior.</param>
    /// <param name="userMessage">User message (including retrieved context).</param>
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Streaming completion: yields tokens one by one, suitable for Server-Sent Events scenarios.
    /// </summary>
    IAsyncEnumerable<string> CompleteStreamAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}
