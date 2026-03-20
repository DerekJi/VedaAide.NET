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

    /// <summary>
    /// 动态生成 System Prompt，注入当前日期，使 LLM 能正确推断"今天/昨天/明天"等相对时间。
    /// </summary>
    private static string BuildSystemPrompt()
    {
        var today = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        return $"""
            你是一个贴心的个人助理，善于根据用户记录的笔记回答问题。
            今天的日期是：{today}。

            回答规则：
            1. 优先依据下方提供的 Context 内容回答，并结合常识进行合理推断。
            2. 回答请使用与用户提问相同的语言（中文提问则用中文回答）。
            3. 如果 Context 中有部分相关信息，请基于已有信息给出最佳推断，并说明推断依据。
            4. 只有在 Context 完全没有任何相关信息时，才回答"我的笔记中没有相关记录"。
            5. 不要重复引用文档名称，直接给出结论。
            """;
    }

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
        var answer = await chatService.CompleteAsync(BuildSystemPrompt(), userMessage, ct);

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

    /// <summary>
    /// 流式问答：先 yield sources，再逐 token yield LLM 输出，最后 yield done（含幻觉标志）。
    /// </summary>
    public async IAsyncEnumerable<RagStreamChunk> QueryStreamAsync(
        RagQueryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        logger.LogInformation("RAG stream query: {Question}", request.Question);

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(request.Question, ct);

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
        {
            yield return new RagStreamChunk { Type = "sources", Sources = [] };
            yield return new RagStreamChunk { Type = "token", Token = "I don't have enough information in the provided documents." };
            yield return new RagStreamChunk { Type = "done", AnswerConfidence = 0f, IsHallucination = false };
            yield break;
        }

        var results = Rerank(candidates, request.Question, request.TopK);

        // 先发送来源列表，让前端立即渲染引用区域
        yield return new RagStreamChunk
        {
            Type = "sources",
            Sources = results.Select(r => new SourceReference
            {
                DocumentName = r.Chunk.DocumentName,
                ChunkContent = r.Chunk.Content.Length > SourceContentMaxLength
                    ? r.Chunk.Content[..SourceContentMaxLength] + "..."
                    : r.Chunk.Content,
                Similarity = r.Similarity
            }).ToList()
        };

        var context = BuildContext(results);
        var userMessage = $"Context:\n{context}\n\nQuestion: {request.Question}";

        // 逐 token 流式输出
        var fullAnswer = new System.Text.StringBuilder();
        await foreach (var token in chatService.CompleteStreamAsync(BuildSystemPrompt(), userMessage, ct))
        {
            fullAnswer.Append(token);
            yield return new RagStreamChunk { Type = "token", Token = token };
        }

        // 防幻觉校验（复用非流式逻辑）
        var answer = fullAnswer.ToString();
        var answerEmbedding = await embeddingService.GenerateEmbeddingAsync(answer, ct);
        var answerCheck = await vectorStore.SearchAsync(answerEmbedding, topK: 1, minSimilarity: 0f, ct: ct);
        var maxSimilarity = answerCheck.Count > 0 ? answerCheck[0].Similarity : 0f;
        var isHallucination = maxSimilarity < options.Value.HallucinationSimilarityThreshold;

        if (!isHallucination && options.Value.EnableSelfCheckGuard)
        {
            var passed = await hallucinationGuard.VerifyAsync(answer, context, ct);
            if (!passed) isHallucination = true;
        }

        if (isHallucination)
            logger.LogWarning("Potential hallucination (stream) for: {Question}", request.Question);

        yield return new RagStreamChunk
        {
            Type = "done",
            AnswerConfidence = results.Max(r => r.Similarity),
            IsHallucination = isHallucination
        };
    }
}
