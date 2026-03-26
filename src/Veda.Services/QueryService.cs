namespace Veda.Services;

/// <summary>
/// RAG 问答查询服务（SRP：只负责检索 + 生成流程）。
/// 依赖：IEmbeddingService、IVectorStore、IChatService、IHallucinationGuardService。
/// </summary>
public sealed class QueryService(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    ILlmRouter llmRouter,
    IHallucinationGuardService hallucinationGuard,
    IContextWindowBuilder contextWindowBuilder,
    IPromptTemplateRepository promptTemplateRepository,
    IChainOfThoughtStrategy chainOfThought,
    ISemanticCache semanticCache,
    IHybridRetriever hybridRetriever,
    ISemanticEnhancer semanticEnhancer,
    IFeedbackBoostService feedbackBoost,
    IOptions<RagOptions> options,
    ILogger<QueryService> logger) : IQueryService
{
    /// <summary>引用来源的最大展示字符数，超出部分截断并追加省略号。</summary>
    internal const int SourceContentMaxLength = 200;

    private const float RerankVectorWeight = 0.7f;
    private const float RerankKeywordWeight = 0.3f;

    /// <summary>
    /// 动态生成 System Prompt：优先从数据库加载 "rag-system" 模板，fallback 到硬编码默认内容。
    /// 模板内容支持 {today} 占位符。
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(CancellationToken ct)
    {
        var today = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        var template = await promptTemplateRepository.GetLatestAsync("rag-system", ct);
        if (template is not null)
            return template.Content.Replace("{today}", today, StringComparison.Ordinal);

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

        // 语义增强：查询扩展（个人词库别名透明补全）
        var expandedQuestion = await semanticEnhancer.ExpandQueryAsync(request.Question, ct);
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(expandedQuestion, ct);

        // 先查语义缓存（命中则跳过向量检索和 LLM 调用）
        var cachedAnswer = await semanticCache.GetAsync(queryEmbedding, ct);
        if (cachedAnswer is not null)
        {
            logger.LogInformation("Semantic cache hit for: {Question}", request.Question);
            return new RagQueryResponse { Answer = cachedAnswer, AnswerConfidence = 1f, IsHallucination = false };
        }

        // 获取 TopK × RerankCandidatesMultiplier 个候选块，为 Reranking 提供更大选择空间。
        var candidateTopK = request.TopK * RagDefaults.RerankCandidatesMultiplier;
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates;

        if (options.Value.HybridRetrievalEnabled)
        {
            var hybridOptions = new HybridRetrievalOptions(
                options.Value.VectorWeight,
                options.Value.KeywordWeight,
                options.Value.FusionStrategy);

            var hybridResults = await hybridRetriever.RetrieveAsync(
                request.Question, queryEmbedding, candidateTopK, hybridOptions,
                scope: request.Scope,
                minSimilarity: request.MinSimilarity,
                filterType: request.FilterDocumentType,
                dateFrom: request.DateFrom,
                dateTo: request.DateTo,
                ct: ct);

            candidates = hybridResults;
        }
        else
        {
            candidates = await vectorStore.SearchAsync(
                queryEmbedding,
                topK: candidateTopK,
                minSimilarity: request.MinSimilarity,
                filterType: request.FilterDocumentType,
                dateFrom: request.DateFrom,
                dateTo: request.DateTo,
                scope: request.Scope,
                ct: ct);
        }

        if (candidates.Count == 0)
            return new RagQueryResponse
            {
                Answer = "I don't have enough information in the provided documents.",
                AnswerConfidence = 0f
            };

        // 轻量重排：70% 向量相似度 + 30% 关键词覆盖率，取前 TopK 个。
        var reranked = Rerank(candidates, request.Question, request.TopK);

        // 反馈 boost：对有正向历史反馈的 chunk 提升排名（无 userId 时跳过）
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> results;
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            var boosted = await feedbackBoost.ApplyBoostAsync(request.UserId, reranked, ct);
            results = boosted;
        }
        else
        {
            results = reranked;
        }

        var contextChunks = contextWindowBuilder.Build(results);
        var context = BuildContext(contextChunks);
        var systemPrompt = await BuildSystemPromptAsync(ct);

        // 结构化输出模式：使用专用 Prompt 强制 LLM 按协议返回 JSON
        string userMessage;
        if (request.StructuredOutput)
        {
            userMessage = BuildStructuredPrompt(request.Question, context, results);
        }
        else
        {
            userMessage = chainOfThought.Enhance(request.Question, context);
        }

        var chatService = llmRouter.Resolve(request.Mode);
        var answer = await chatService.CompleteAsync(systemPrompt, userMessage, ct);

        // 尝试解析结构化输出
        StructuredFinding? structuredFinding = null;
        if (request.StructuredOutput)
        {
            var parser = new StructuredOutputParser();
            structuredFinding = parser.TryParse(answer, results);
        }

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

        // 非幻觉答案写入语义缓存
        if (!isHallucination)
            await semanticCache.SetAsync(queryEmbedding, answer, ct);

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
            AnswerConfidence = results.Max(r => r.Similarity),
            StructuredOutput = structuredFinding
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
    /// 从 Token 预算裁剪后的先纯块列表构建上下文字符串（ContextWindowBuilder 输出使用）。
    /// </summary>
    private static string BuildContext(IReadOnlyList<DocumentChunk> chunks)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            sb.AppendLine($"[{i + 1}] Source: {chunks[i].DocumentName}");
            sb.AppendLine(chunks[i].Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>构建结构化输出 Prompt，强制 LLM 返回特定 JSON 格式。</summary>
    private static string BuildStructuredPrompt(
        string question,
        string context,
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> sources)
    {
        return $$"""
            Context:
            {{context}}

            Question: {{question}}

            请基于上述 Context 给出结构化推理，严格按照以下 JSON 格式输出（不要有其他文字）：
            {
              "type": "Information | Warning | Conflict | HighRisk",
              "summary": "结论摘要（1-2句话）",
              "evidence": ["来源文档名或关键摘要片段1", "来源2"],
              "counterEvidence": ["若有矛盾证据则列出，否则省略此字段"],
              "confidence": 0.85,
              "uncertaintyNote": "若置信度低于0.7请说明原因，否则省略"
            }
            """;
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

        // 语义增强：流式查询也应用查询扩展
        var expandedQuestion = await semanticEnhancer.ExpandQueryAsync(request.Question, ct);
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(expandedQuestion, ct);

        // 先查语义缓存（命中则直接流式返回缓存答案）
        var cachedAnswer = await semanticCache.GetAsync(queryEmbedding, ct);
        if (cachedAnswer is not null)
        {
            logger.LogInformation("Semantic cache hit (stream) for: {Question}", request.Question);
            yield return new RagStreamChunk { Type = "sources", Sources = [] };
            yield return new RagStreamChunk { Type = "token", Token = cachedAnswer };
            yield return new RagStreamChunk { Type = "done", AnswerConfidence = 1f, IsHallucination = false };
            yield break;
        }

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

        var contextChunks = contextWindowBuilder.Build(results);
        var context = BuildContext(contextChunks);
        var systemPrompt = await BuildSystemPromptAsync(ct);
        var userMessage = chainOfThought.Enhance(request.Question, context);

        // 逐 token 流式输出
        var chatService = llmRouter.Resolve(request.Mode);
        var fullAnswer = new System.Text.StringBuilder();
        await foreach (var token in chatService.CompleteStreamAsync(systemPrompt, userMessage, ct))
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

        // 非幻觉答案写入语义缓存
        if (!isHallucination)
            await semanticCache.SetAsync(queryEmbedding, answer, ct);

        yield return new RagStreamChunk
        {
            Type = "done",
            AnswerConfidence = results.Max(r => r.Similarity),
            IsHallucination = isHallucination
        };
    }
}
