using System.Runtime.CompilerServices;
using Veda.Core.Interfaces;

namespace Veda.Services.Tests.Integration;

/// <summary>
/// Chat service stub for integration tests — no LLM required.
/// Echoes the userMessage back as the answer, so assertions can verify
/// which document chunks were surfaced by the vector search step.
/// </summary>
internal sealed class FakeChatService : IChatService
{
    public Task<string> CompleteAsync(
        string systemPrompt, string userMessage, CancellationToken ct = default)
        => Task.FromResult($"[FakeLLM] {userMessage}");

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt, string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return $"[FakeLLM] {userMessage}";
        await Task.CompletedTask;
    }
}
