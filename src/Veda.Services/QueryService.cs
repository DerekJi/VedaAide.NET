namespace Veda.Services;

/// <summary>
/// RAG 问答查询服务（SRP：只负责检索 + 生成流程）。
/// 依赖：IEmbeddingService、IVectorStore、IChatService、IHallucinationGuardService。
/// </summary>
public sealed class QueryService(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    IChatService chatService,
    IHallucinationGuardService hallucinationGuard,
    IOptions<RagOptions> options,
    ILogger<QueryService> logger) : IQueryService
{
    /// <summary>引用来源的最大展示字符数，超出部分截断并追加省略号。</summary>
    internal const int SourceContentMaxLength = 200;

    private const float RerankVectorWeight = 0.7f;
    private const float RerankKeywordWeight = 0.3f;

    private const string SystemPrompt =
        """
        You are a precise question-answering assistant.
        Answer ONLY based on the provided context chunks below.
        If the answer is not in the context, say "I don't have enough information in the provided documents."
        Always cite the document name when referencing information.
        """;

    public async Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        logger.LogInformation("RAG query: {Question}", request.Question);

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(request.Question, ct);

        // 获取 TopK × RerankCandidatesMultiplier 个候选块，为 Reranking 提供更大选择空间。
        var candidateTopK = request.TopK * RagDefaults.RerankCandidatesMultiplier;
        var candidates = await vectorStore.SearchAsync(
            queryEmbedding,
            topK: candidateTopK,
            minSimilarity: request.MinSimilarity,
            filterType: request.FilterDocumentType,
            dateFrom: request.DateFrom,
            dateTo: request.DateTo,
            ct: ct);

        if (candidates.Count == 0)
            return new RagQueryResponse
            {
                Answer = "I don't have enough information in the provided documents.",
                AnswerConfidence = 0f
            };

        // 轻量重排：70% 向量相似度 + 30% 關鍵词覆盖率，取前 TopK 个。
        var results = Rerank(candidates, request.Question, request.TopK);

        var context = BuildContext(results);
        var userMessage = $"Context:\n{context}\n\nQuestion: {request.Question}";
        var answer = await chatService.CompleteAsync(SystemPrompt, userMessage, ct);

        // 防幻觉第一层：回答 Embedding 与向量库相似度校验。
        var answerEmbedding = await embeddingService.GenerateEmbeddingAsync(answer, ct);
        var answerCheck = await vectorStore.SearchAsync(answerEmbedding, topK: 1, minSimilarity: 0f, ct: ct);
        var maxAnswerSimilarity = answerCheck.Count > 0 ? answerCheck[0].Similarity : 0f;
        var isHallucination = maxAnswerSimilarity < options.Value.HallucinationSimilarityThreshold;

        // 防幻觉第二层（可选）：LLM 自我校验。
        if (!isHallucination && options.Value.EnableSelfCheckGuard)
        {
            var selfCheckPassed = await hallucinationGuard.VerifyAsync(answer, context, ct);
            if (!selfCheckPassed)
                isHallucination = true;
        }

        if (isHallucination)
            logger.LogWarning("Potential hallucination detected for question: {Question}", request.Question);

        return new RagQueryResponse
        {
            Answer = answer,
            IsHallucination = isHallucination,
            Sources = results.Select(r => new SourceReference
            {
                DocumentName = r.Chunk.DocumentName,
                ChunkContent = r.Chunk.Content.Length > SourceContentMaxLength
                    ? r.Chunk.Content[..SourceContentMaxLength] + "..."
                    : r.Chunk.Content,
                Similarity = r.Similarity
            }).ToList(),
            AnswerConfidence = results.Max(r => r.Similarity)
        };
    }

    /// <summary>
    /// 轻量重排：70% 向量相似度 + 30% 问题关键词覆盖率。
    /// 不需额外 LLM 调用， Phase 4 可替换为 cross-encoder 模型。
    /// </summary>
    private static IReadOnlyList<(DocumentChunk Chunk, float Similarity)> Rerank(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        string question,
        int topK)
    {
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
                var combined = RerankVectorWeight * c.Similarity + RerankKeywordWeight * overlapScore;
                return (c.Chunk, combined);
            })
            .OrderByDescending(x => x.combined)
            .Take(topK)
            .ToList();
    }

    private static string BuildContext(IReadOnlyList<(DocumentChunk Chunk, float Similarity)> results)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < results.Count; i++)
        {
            var (chunk, score) = results[i];
            sb.AppendLine($"[{i + 1}] Source: {chunk.DocumentName} (similarity: {score:P0})");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
