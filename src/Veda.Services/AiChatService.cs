using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using Veda.Core.Interfaces;

namespace Veda.Services;

/// <summary>
/// DIP adapter: wraps the Microsoft.Extensions.AI's IChatClient as the domain interface IChatService.
/// Also captures token usage metadata and writes it to ITokenUsageRepository.
/// This is the MAF-compatible replacement for OllamaChatService.
/// </summary>
public sealed class AiChatService(
    IChatClient inner,
    ITokenUsageRepository? usageRepo = null,
    ICurrentUserService? currentUser = null) : IChatService
{
    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userMessage)
        };
        
        var response = await inner.GetResponseAsync(messages, cancellationToken: ct);

        _ = TryRecordUsageAsync(response.ModelId ?? "llm", "Chat", response.Usage, ct: CancellationToken.None);

        return response.Text ?? string.Empty;
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userMessage)
        };

        string? modelId = null;
        UsageDetails? lastUsage = null;

        await foreach (var chunk in inner.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(chunk.Text))
                yield return chunk.Text;

            modelId ??= chunk.ModelId;
            if (chunk.AdditionalProperties is { Count: > 0 } props &&
                props.TryGetValue("Usage", out var usageObj) && usageObj is UsageDetails ud)
                lastUsage = ud;
        }

        // Record usage after the stream ends (the last chunk's AdditionalProperties usually contains usage)
        _ = TryRecordUsageAsync(modelId ?? "llm", "Chat", lastUsage, ct: CancellationToken.None);
    }

    private Task TryRecordUsageAsync(
        string modelName, string opType, UsageDetails? usage, CancellationToken ct)
    {
        if (usageRepo is null || usage is null) return Task.CompletedTask;

        int prompt     = (int)(usage.InputTokenCount  ?? 0);
        int completion = (int)(usage.OutputTokenCount ?? 0);
        if (prompt == 0 && completion == 0) return Task.CompletedTask;

        var userId = currentUser?.UserId ?? "anonymous";
        return usageRepo.RecordAsync(new TokenUsageRecord(userId, modelName, opType, prompt, completion), ct);
    }
}
