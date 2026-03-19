namespace Veda.Services;

/// <summary>
/// 防幻觉第二层：调用 LLM 逐句审核回答是否有文档依据。
/// 由 Veda:Rag:EnableSelfCheckGuard 配置项控制是否调用。
/// </summary>
public sealed class HallucinationGuardService(IChatService chatService) : IHallucinationGuardService
{
    private const string SystemPrompt =
        """
        You are a strict fact-checking assistant.
        Your task: determine whether the Answer below is FULLY SUPPORTED by the provided Context.
        Rules:
        - Respond ONLY with "true" if every claim in the Answer can be found in or directly inferred from the Context.
        - Respond ONLY with "false" if the Answer contains ANY claim not present in the Context.
        - Do not add any explanation or extra text.
        """;

    public async Task<bool> VerifyAsync(string answer, string context, CancellationToken ct = default)
    {
        var userMessage = $"Context:\n{context}\n\nAnswer to verify:\n{answer}";
        var response = await chatService.CompleteAsync(SystemPrompt, userMessage, ct);
        return response.Trim().StartsWith("true", StringComparison.OrdinalIgnoreCase);
    }
}
