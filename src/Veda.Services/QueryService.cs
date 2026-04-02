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
    /// 模板内容支持 {today} 占位符。根据问题语言自动调整语言规则指令。
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(string question, CancellationToken ct)
    {
        var today = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        var template = await promptTemplateRepository.GetLatestAsync("rag-system", ct);
        if (template is not null)
            return template.Content.Replace("{today}", today, StringComparison.Ordinal);

        var langRule = IsChinese(question)
            ? "2. 必须使用中文回答。"
            : "2. You MUST respond entirely in English. Do not use Chinese.";

        return $"""
            你是一个贴心的个人助理，善于根据用户记录的笔记回答问题。
            今天的日期是：{today}。

            回答规则：
            1. 优先依据下方提供的 Context 内容回答，并结合常识进行合理推断。
            {langRule}
            3. 如果 Context 中有部分相关信息，请基于已有信息给出最佳推断，并说明推断依据。
            4. 只有在 Context 完全没有任何相关信息时，才回答无相关记录。
            5. 不要重复引用文档名称，直接给出结论。
            """;
    }

    /// <summary>
    /// 简单启发式语言检测：CJK 字符占比超过 33% 则视为中文。
    /// </summary>
    private static bool IsChinese(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var cjkCount = text.Count(c => c >= '\u4E00' && c <= '\u9FFF');
        return cjkCount * 3 > text.Length;
    }

    public async Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        logger.LogInformation("RAG query: {Question}", request.Question);

        // 语义增强：查询扩展（个人词库别名透明补全）
        var expandedQuestion = await semanticEnhancer.ExpandQueryAsync(request.Question, ct);
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(expandedQuestion, ct);

        // 先查语义缓存（命中则跳过向量检索和 LLM 调用）
        // EphemeralContext 存在时跳过缓存：附件内容使同一问题产生不同答案，缓存会返回错误结果。
        var cachedAnswer = string.IsNullOrWhiteSpace(request.EphemeralContext)
            ? await semanticCache.GetAsync(queryEmbedding, ct)
            : null;
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

        // 有 EphemeralContext 时即使向量库无命中也继续生成，附件内容本身即上下文。
        if (candidates.Count == 0 && string.IsNullOrWhiteSpace(request.EphemeralContext))
            return new RagQueryResponse
            {
                Answer = "I don't have enough information in the provided documents.",
                AnswerConfidence = 0f
            };

        // 轻量重排：70% 向量相似度 + 30% 关键词覆盖率，取前 TopK 个。
        var reranked = candidates.Count > 0
            ? Rerank(candidates, request.Question, request.TopK)
            : [];

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

        // Ephemeral RAG：将临时上传内容前置注入 context
        if (!string.IsNullOrWhiteSpace(request.EphemeralContext))
            context = BuildEphemeralPrefix(request.EphemeralContext) + context;

        var systemPrompt = await BuildSystemPromptAsync(request.Question, ct);

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

        // 防幻觉：携带 EphemeralContext 时跳过相似度检测（临时内容无向量评分）
        bool isHallucination;
        if (!string.IsNullOrWhiteSpace(request.EphemeralContext))
        {
            isHallucination = false;
        }
        else
        {
            // 防幻觉第一层：基于检索到的 source chunks 与 query 的相似度。
            var maxAnswerSimilarity = results.Count > 0 ? results.Max(r => r.Similarity) : 0f;
            isHallucination = maxAnswerSimilarity < options.Value.HallucinationSimilarityThreshold;

            // 防幻觉第二层（可选）：LLM 自我校验。
            if (!isHallucination && options.Value.EnableSelfCheckGuard)
            {
                var selfCheckPassed = await hallucinationGuard.VerifyAsync(answer, context, ct);
                if (!selfCheckPassed)
                    isHallucination = true;
            }
        }

        if (isHallucination)
            logger.LogWarning("Potential hallucination detected for question: {Question}", request.Question);

        // 非幻觉答案写入语义缓存（EphemeralContext 不缓存，避免污染后续无附件请求）
        if (!isHallucination && string.IsNullOrWhiteSpace(request.EphemeralContext))
            await semanticCache.SetAsync(queryEmbedding, answer, ct);

        var confidence = results.Count > 0 ? results.Max(r => r.Similarity) : 0f;
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
                Similarity   = r.Similarity,
                ChunkId      = r.Chunk.Id,
                DocumentId   = r.Chunk.DocumentId
            }).ToList(),
            AnswerConfidence = confidence,
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
        // EphemeralContext 存在时跳过缓存：附件内容使同一问题产生不同答案，缓存会返回错误结果。
        var cachedAnswer = string.IsNullOrWhiteSpace(request.EphemeralContext)
            ? await semanticCache.GetAsync(queryEmbedding, ct)
            : null;
        if (cachedAnswer is not null)
        {
            logger.LogInformation("Semantic cache hit (stream) for: {Question}", request.Question);
            yield return new RagStreamChunk { Type = "sources", Sources = [] };
            yield return new RagStreamChunk { Type = "token", Token = cachedAnswer };
            yield return new RagStreamChunk { Type = "done", AnswerConfidence = 1f, IsHallucination = false };
            yield break;
        }

        var candidateTopK = request.TopK * RagDefaults.RerankCandidatesMultiplier;
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates;

        if (options.Value.HybridRetrievalEnabled)
        {
            var hybridOptions = new HybridRetrievalOptions(
                options.Value.VectorWeight,
                options.Value.KeywordWeight,
                options.Value.FusionStrategy);

            candidates = await hybridRetriever.RetrieveAsync(
                request.Question, queryEmbedding, candidateTopK, hybridOptions,
                scope: request.Scope,
                minSimilarity: request.MinSimilarity,
                filterType: request.FilterDocumentType,
                dateFrom: request.DateFrom,
                dateTo: request.DateTo,
                ct: ct);
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

        // 有 EphemeralContext 时即使向量库无命中也继续生成，附件内容本身即上下文。
        if (candidates.Count == 0 && string.IsNullOrWhiteSpace(request.EphemeralContext))
        {
            yield return new RagStreamChunk { Type = "sources", Sources = [] };
            yield return new RagStreamChunk { Type = "token", Token = "I don't have enough information in the provided documents." };
            yield return new RagStreamChunk { Type = "done", AnswerConfidence = 0f, IsHallucination = false };
            yield break;
        }

        var reranked = candidates.Count > 0
            ? Rerank(candidates, request.Question, request.TopK)
            : [];

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
                Similarity   = r.Similarity,
                ChunkId      = r.Chunk.Id,
                DocumentId   = r.Chunk.DocumentId
            }).ToList()
        };

        var contextChunks = contextWindowBuilder.Build(results);
        var context = BuildContext(contextChunks);

        // Ephemeral RAG：将临时上传内容前置注入 context
        if (!string.IsNullOrWhiteSpace(request.EphemeralContext))
            context = BuildEphemeralPrefix(request.EphemeralContext) + context;

        var systemPrompt = await BuildSystemPromptAsync(request.Question, ct);
        var userMessage = chainOfThought.Enhance(request.Question, context);

        // 逐 token 流式输出
        var chatService = llmRouter.Resolve(request.Mode);
        var fullAnswer = new System.Text.StringBuilder();
        await foreach (var token in chatService.CompleteStreamAsync(systemPrompt, userMessage, ct))
        {
            fullAnswer.Append(token);
            yield return new RagStreamChunk { Type = "token", Token = token };
        }

        var answer = fullAnswer.ToString();

        // 防幻觉：携带 EphemeralContext 时跳过相似度检测
        bool isHallucination;
        if (!string.IsNullOrWhiteSpace(request.EphemeralContext))
        {
            isHallucination = false;
        }
        else
        {
            var maxSimilarity = results.Count > 0 ? results.Max(r => r.Similarity) : 0f;
            isHallucination = maxSimilarity < options.Value.HallucinationSimilarityThreshold;

            if (!isHallucination && options.Value.EnableSelfCheckGuard)
            {
                var passed = await hallucinationGuard.VerifyAsync(answer, context, ct);
                if (!passed) isHallucination = true;
            }
        }

        if (isHallucination)
            logger.LogWarning("Potential hallucination (stream) for: {Question}", request.Question);

        // EphemeralContext 不写缓存，避免污染后续无附件请求
        if (!isHallucination && string.IsNullOrWhiteSpace(request.EphemeralContext))
            await semanticCache.SetAsync(queryEmbedding, answer, ct);

        var confidence = results.Count > 0 ? results.Max(r => r.Similarity) : 0f;
        yield return new RagStreamChunk
        {
            Type = "done",
            AnswerConfidence = confidence,
            IsHallucination = isHallucination
        };
    }

    /// <summary>构建临时附件上下文前缀，与 KB 检索结果区隔。</summary>
    private static string BuildEphemeralPrefix(string ephemeralContext) =>
        $"[临时上传文件内容 — 仅供本次问答，不写入知识库]\n{ephemeralContext}\n---（以下为知识库检索结果）---\n";
}
