using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Runtime.CompilerServices;
using Veda.Core.Interfaces;

namespace Veda.Services;

/// <summary>
/// DIP 适配器：将 SK 框架的 IChatCompletionService 包装为领域接口 IChatService。
/// 同时捕获 SK 返回的 token 消耗 Metadata，写入 ITokenUsageRepository。
/// </summary>
public sealed class OllamaChatService(
    IChatCompletionService inner,
    ITokenUsageRepository? usageRepo = null,
    ICurrentUserService? currentUser = null) : IChatService
{
    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var history = new ChatHistory(systemPrompt);
        history.AddUserMessage(userMessage);
        var response = await inner.GetChatMessageContentAsync(history, cancellationToken: ct);

        _ = TryRecordUsageAsync(response.ModelId ?? "llm", "Chat",
            response.Metadata, ct: CancellationToken.None);

        return response.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var history = new ChatHistory(systemPrompt);
        history.AddUserMessage(userMessage);

        string? modelId = null;
        IReadOnlyDictionary<string, object?>? lastMetadata = null;

        await foreach (var chunk in inner.GetStreamingChatMessageContentsAsync(history, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
                yield return chunk.Content;

            modelId ??= chunk.ModelId;
            if (chunk.Metadata is { Count: > 0 })
                lastMetadata = chunk.Metadata;
        }

        // 流结束后记录 usage（最后一个 chunk 的 Metadata 通常含 usage）
        _ = TryRecordUsageAsync(modelId ?? "llm", "Chat", lastMetadata, ct: CancellationToken.None);
    }

    private Task TryRecordUsageAsync(
        string modelName, string opType,
        IReadOnlyDictionary<string, object?>? metadata,
        CancellationToken ct)
    {
        if (usageRepo is null || metadata is null) return Task.CompletedTask;
        if (!metadata.TryGetValue("Usage", out var usageObj) || usageObj is null) return Task.CompletedTask;

        int prompt = 0, completion = 0;
        // M.E.AI path (Ollama SK connector wraps IChatClient → response.Usage is UsageDetails with long? properties)
        if (usageObj is UsageDetails ud)
        {
            prompt     = (int)(ud.InputTokenCount  ?? 0);
            completion = (int)(ud.OutputTokenCount ?? 0);
        }
        else
        {
            // Azure OpenAI native SK connector path (CompletionsUsage with int PromptTokens/CompletionTokens)
            try
            {
                dynamic u = usageObj;
                try { prompt     = (int)u.InputTokenCount; }  catch { try { prompt     = (int)u.PromptTokenCount;    } catch { } }
                try { completion = (int)u.OutputTokenCount; } catch { try { completion = (int)u.CompletionTokenCount; } catch { } }
            }
            catch { return Task.CompletedTask; }
        }

        if (prompt == 0 && completion == 0) return Task.CompletedTask;

        var userId = currentUser?.UserId ?? "anonymous";
        return usageRepo.RecordAsync(new TokenUsageRecord(userId, modelName, opType, prompt, completion), ct);
    }
}
