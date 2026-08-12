using Veda.Core.Options;
namespace Veda.Services;

/// <summary>
/// Shared helper service for RAG queries: provides common logic such as retrieval, reranking,
/// and context building. Shared by QueryService and QueryStreamService to avoid code duplication.
/// </summary>
public sealed class RagQueryHelper(
    IVectorStore vectorStore,
    IHybridRetriever hybridRetriever,
    IFeedbackBoostService feedbackBoost,
    IContextWindowBuilder contextWindowBuilder,
    IHallucinationGuardService hallucinationGuard,
    IOptions<RagOptions> options,
    ILogger logger) : IRagQueryHelper
{
    /// <summary>Maximum display characters for a cited source.</summary>
    internal const int SourceContentMaxLength = 200;

    /// <summary>
    /// Retrieves candidates: picks hybrid or vector retrieval based on configuration.
    /// </summary>
    public async Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> RetrieveCandidatesAsync(
        string expandedQuestion,
        float[] queryEmbedding,
        RagQueryRequest request,
        CancellationToken ct)
    {
        logger.LogInformation("Expanded question: {ExpandedQuestion}", expandedQuestion);
        logger.LogInformation("Embedding vector length: {Length}", queryEmbedding.Length);

        var candidateTopK = request.TopK * RagDefaults.RerankCandidatesMultiplier;

        return options.Value.HybridRetrievalEnabled
            ? await RetrieveWithHybridAsync(expandedQuestion, queryEmbedding, candidateTopK, request, ct)
            : await RetrieveWithVectorAsync(queryEmbedding, candidateTopK, request, ct);
    }

    /// <summary>Hybrid retrieval: vector + keyword.</summary>
    private async Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> RetrieveWithHybridAsync(
        string expandedQuestion,
        float[] queryEmbedding,
        int candidateTopK,
        RagQueryRequest request,
        CancellationToken ct)
    {
        var hybridOptions = new HybridRetrievalOptions(
            options.Value.VectorWeight,
            options.Value.KeywordWeight,
            options.Value.FusionStrategy);

        return await hybridRetriever.RetrieveAsync(
            expandedQuestion, queryEmbedding, candidateTopK, hybridOptions,
            scope: request.Scope,
            minSimilarity: request.MinSimilarity,
            filterType: request.FilterDocumentType,
            dateFrom: request.DateFrom,
            dateTo: request.DateTo,
            ct: ct);
    }

    /// <summary>Pure vector retrieval.</summary>
    private async Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> RetrieveWithVectorAsync(
        float[] queryEmbedding,
        int candidateTopK,
        RagQueryRequest request,
        CancellationToken ct)
    {
        return await vectorStore.SearchAsync(
            queryEmbedding,
            topK: candidateTopK,
            minSimilarity: request.MinSimilarity,
            filterType: request.FilterDocumentType,
            dateFrom: request.DateFrom,
            dateTo: request.DateTo,
            scope: request.Scope,
            ct: ct);
    }

    /// <summary>
    /// Reranking and feedback boost: applies the user-feedback boost after a light rerank.
    /// </summary>
    public async Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> RerankAndBoostAsync(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        string question,
        int topK,
        string? userId,
        CancellationToken ct)
    {
        if (candidates.Count == 0)
            return [];

        // Light rerank
        var reranked = Rerank(candidates, question, topK)
            .Select(c => (c.Chunk, Score: c.Similarity))
            .ToList();

        // Apply the feedback boost
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return await feedbackBoost.ApplyBoostAsync(userId, reranked, ct);
        }

        return reranked;
    }

    /// <summary>
    /// Light rerank: 70% vector similarity + 30% question-keyword coverage.
    /// Requires no extra LLM call; can be swapped for a cross-encoder model in Phase 4.
    /// </summary>
    public IReadOnlyList<(DocumentChunk Chunk, float Similarity)> Rerank(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        string question,
        int topK)
    {
        const float rerankVectorWeight = 0.7f;
        const float rerankKeywordWeight = 0.3f;

        var questionWords = question
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        return candidates
            .Select(c =>
            {
                var contentWords = c.Chunk.Content
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => w.ToLowerInvariant());
                var overlapScore = questionWords.Count > 0
                    ? (float)contentWords.Count(w => questionWords.Contains(w)) / questionWords.Count
                    : 0f;
                var combined = rerankVectorWeight * c.Similarity + rerankKeywordWeight * overlapScore;
                return (c.Chunk, combined);
            })
            .OrderByDescending(x => x.combined)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Builds the context from the list of text chunks trimmed to the token budget.
    /// </summary>
    public string BuildContext(IReadOnlyList<DocumentChunk> chunks, string? ephemeralContext = null)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(ephemeralContext))
            sb.AppendLine(BuildEphemeralPrefix(ephemeralContext));

        for (var i = 0; i < chunks.Count; i++)
        {
            sb.AppendLine($"[{i + 1}] Source: {chunks[i].DocumentName}");
            sb.AppendLine(chunks[i].Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Detects whether the answer is a hallucination.
    /// </summary>
    public async Task<bool> DetectHallucinationAsync(
        string answer,
        string context,
        RagQueryRequest request,
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> results,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.EphemeralContext))
            return false;

        var maxSimilarity = results.Count > 0 ? results.Max(r => r.Similarity) : 0f;
        var isHallucination = maxSimilarity < options.Value.HallucinationSimilarityThreshold;

        if (!isHallucination && options.Value.EnableSelfCheckGuard)
        {
            var passed = await hallucinationGuard.VerifyAsync(answer, context, ct);
            if (!passed) isHallucination = true;
        }

        if (isHallucination)
            logger.LogWarning("Potential hallucination detected for question: {Question}", request.Question);

        return isHallucination;
    }

    /// <summary>Builds the ephemeral attachment context prefix.</summary>
    private static string BuildEphemeralPrefix(string ephemeralContext) =>
        $"[临时上传文件内容 — 仅供本次问答，不写入知识库]\n{ephemeralContext}\n---（以下为知识库检索结果）---\n";
}
