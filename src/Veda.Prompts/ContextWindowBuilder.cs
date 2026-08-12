namespace Veda.Prompts;

/// <summary>
/// Selects the optimal context set from the candidate chunks given a token budget.
/// Rough estimate: 1 token ≈ 4 characters (English) / 2 characters (Chinese). A conservative 3 chars/token is used
/// to ensure the LLM context window is never exceeded.
/// </summary>
public sealed class ContextWindowBuilder : IContextWindowBuilder
{
    // Conservative estimate: 3 chars/token, to avoid truncating Chinese content
    private const int CharsPerToken = 3;

    public IReadOnlyList<DocumentChunk> Build(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        int maxTokens = 3000)
    {
        var charBudget = maxTokens * CharsPerToken;
        var selected = new List<DocumentChunk>(capacity: candidates.Count);
        var usedChars = 0;

        // Select chunks in descending similarity order until the budget is exhausted
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
