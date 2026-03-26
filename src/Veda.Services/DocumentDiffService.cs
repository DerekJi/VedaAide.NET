namespace Veda.Services;

/// <summary>
/// 基于 LLM 的文档版本对比服务。
/// 通过分析新旧内容的关键词差异生成结构化变更摘要。
/// </summary>
public sealed class DocumentDiffService(IChatService chatService) : IDocumentDiffService
{
    public async Task<DocumentChangeSummary> DiffAsync(
        string documentId,
        string oldContent,
        string newContent,
        CancellationToken ct = default)
    {
        // 向量差异估算：通过词集合差异统计变化
        var oldWords = GetWordSet(oldContent);
        var newWords = GetWordSet(newContent);

        var added   = newWords.Except(oldWords).Count();
        var removed = oldWords.Except(newWords).Count();
        var shared  = oldWords.Intersect(newWords).Count();
        var total   = Math.Max(oldWords.Count, newWords.Count);
        var changeRatio = total > 0 ? (float)(added + removed) / (total * 2) : 0f;
        var modifiedChunks = (int)Math.Round(changeRatio * 5); // 估算修改 chunk 数量

        // 使用 LLM 生成变更主题摘要
        var changedTopics = await ExtractChangedTopicsAsync(oldContent, newContent, ct);

        return new DocumentChangeSummary(
            documentId,
            AddedChunks: Math.Max(0, added / 50),
            RemovedChunks: Math.Max(0, removed / 50),
            ModifiedChunks: modifiedChunks,
            changedTopics,
            DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<string>> ExtractChangedTopicsAsync(
        string oldContent, string newContent, CancellationToken ct)
    {
        var system = "你是一个文档差异分析助手，请用简洁的 JSON 数组格式列出主要变更主题。";
        var prompt = $"""
            请分析以下两个文档版本的主要变更，返回一个 JSON 字符串数组，每项为一个变更主题（不超过 5 项）。

            旧版本前 500 字：
            {oldContent[..Math.Min(500, oldContent.Length)]}

            新版本前 500 字：
            {newContent[..Math.Min(500, newContent.Length)]}

            只返回 JSON 数组，例如：["数值更新","新增章节","删除过时内容"]
            """;

        try
        {
            var result = await chatService.CompleteAsync(system, prompt, ct);
            // 简单提取 JSON 数组
            var match = System.Text.RegularExpressions.Regex.Match(result, @"\[.*?\]", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (match.Success)
            {
                var topics = System.Text.Json.JsonSerializer.Deserialize<List<string>>(match.Value);
                if (topics is not null) return topics.AsReadOnly();
            }
        }
        catch
        {
            // 降级：返回空列表
        }

        return [];
    }

    private static HashSet<string> GetWordSet(string text)
        => text.Split([' ', '\n', '\r', '\t', '，', '。', '、'], StringSplitOptions.RemoveEmptyEntries)
               .Select(w => w.ToLowerInvariant())
               .Where(w => w.Length >= 2)
               .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
