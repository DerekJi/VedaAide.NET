namespace Veda.Services;

/// <summary>
/// RAG 查询的共享辅助服务：提供检索、排名、上下文构建等公共逻辑。
/// 供 QueryService 和 QueryStreamService 共用，避免代码重复。
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
    /// <summary>引用来源的最大展示字符数。</summary>
    internal const int SourceContentMaxLength = 200;

    /// <summary>
    /// 检索候选：根据配置选择混合检索或向量检索。
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

    /// <summary>混合检索：向量 + 关键词。</summary>
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

    /// <summary>纯向量检索。</summary>
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
    /// 排名与反馈 boost：轻量重排后应用用户反馈 boost。
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

        // 轻量重排
        var reranked = Rerank(candidates, question, topK)
            .Select(c => (c.Chunk, Score: c.Similarity))
            .ToList();

        // 应用反馈 boost
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return await feedbackBoost.ApplyBoostAsync(userId, reranked, ct);
        }

        return reranked;
    }

    /// <summary>
    /// 轻量重排：70% 向量相似度 + 30% 问题关键词覆盖率。
    /// 不需额外 LLM 调用，Phase 4 可替换为 cross-encoder 模型。
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
    /// 构建上下文：从 Token 预算裁剪后的文本块列表构建上下文。
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
    /// 检测答案是否为幻觉。
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

    /// <summary>构建临时附件上下文前缀。</summary>
    private static string BuildEphemeralPrefix(string ephemeralContext) =>
        $"[临时上传文件内容 — 仅供本次问答，不写入知识库]\n{ephemeralContext}\n---（以下为知识库检索结果）---\n";
}
