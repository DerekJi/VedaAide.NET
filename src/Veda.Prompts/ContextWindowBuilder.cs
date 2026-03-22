namespace Veda.Prompts;

/// <summary>
/// 根据 Token 预算从候选文档块中选取最优上下文集合。
/// 粗略估算：1 token ≈ 4 个字符（英文）/ 2 个字符（中文）。取 3 chars/token 作为保守估算，
/// 确保不超出 LLM 上下文窗口。
/// </summary>
public sealed class ContextWindowBuilder : IContextWindowBuilder
{
    // 保守估算：3 字符/token，避免截断中文内容
    private const int CharsPerToken = 3;

    public IReadOnlyList<DocumentChunk> Build(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        int maxTokens = 3000)
    {
        var charBudget = maxTokens * CharsPerToken;
        var selected = new List<DocumentChunk>(capacity: candidates.Count);
        var usedChars = 0;

        // 按相似度降序选取，直至耗尽预算
        foreach (var (chunk, _) in candidates.OrderByDescending(x => x.Similarity))
        {
            if (usedChars + chunk.Content.Length > charBudget)
                break;

            selected.Add(chunk);
            usedChars += chunk.Content.Length;
        }

        return selected.AsReadOnly();
    }
}
