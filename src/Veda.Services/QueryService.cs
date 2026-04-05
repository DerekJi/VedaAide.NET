namespace Veda.Services;

/// <summary>
/// RAG 同步问答查询服务（SRP：只负责检索 + 生成完整答案）。
/// </summary>
public sealed class QueryService(
    IEmbeddingService embeddingService,
    ILlmRouter llmRouter,
    IContextWindowBuilder contextWindowBuilder,
    IPromptTemplateRepository promptTemplateRepository,
    IChainOfThoughtStrategy chainOfThought,
    ISemanticCache semanticCache,
    ISemanticEnhancer semanticEnhancer,
    ILogger<QueryService> logger,
    IRagQueryHelper helper) : IQueryService
{

    /// <summary>
    /// 动态生成 System Prompt：优先从数据库加载 "rag-system" 模板，fallback 到硬编码默认内容。
    /// 模板内容支持 {today} 占位符。根据问题语言自动调整语言规则指令。
    /// </summary>
    internal async Task<string> BuildSystemPromptAsync(string question, CancellationToken ct)
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

        // 语义增强：查询扩展
        var expandedQuestion = await semanticEnhancer.ExpandQueryAsync(request.Question, ct);
        var embeddingVector = await embeddingService.GenerateEmbeddingAsync(expandedQuestion, ct);
        logger.LogInformation("Generated embeddingVector with length: {Length}", embeddingVector.Length);

        // 检查语义缓存
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(expandedQuestion, ct);
        var cachedAnswer = string.IsNullOrWhiteSpace(request.EphemeralContext)
            ? await semanticCache.GetAsync(queryEmbedding, ct)
            : null;
        if (cachedAnswer is not null)
        {
            logger.LogInformation("Semantic cache hit for: {Question}", request.Question);
            return new RagQueryResponse { Answer = cachedAnswer, AnswerConfidence = 1f, IsHallucination = false };
        }

        // 检索并排名
        var candidates = await helper.RetrieveCandidatesAsync(expandedQuestion, embeddingVector, request, ct);
        var rerankedResults = await helper.RerankAndBoostAsync(candidates, request.Question, request.TopK, request.UserId, ct);

        // No results and no ephemeral context: return early with no-info message
        if (rerankedResults.Count == 0 && string.IsNullOrWhiteSpace(request.EphemeralContext))
        {
            return new RagQueryResponse
            {
                Answer = "I don't have enough information in the provided documents.",
                AnswerConfidence = 0f,
                IsHallucination = false,
                Sources = []
            };
        }

        // 构建上下文并生成答案
        var contextChunks = contextWindowBuilder.Build(rerankedResults);
        var context = helper.BuildContext(contextChunks, request.EphemeralContext);
        var systemPrompt = await BuildSystemPromptAsync(request.Question, ct);

        string userMessage;
        if (request.StructuredOutput)
        {
            userMessage = BuildStructuredPrompt(request.Question, context, []);
        }
        else
        {
            userMessage = chainOfThought.Enhance(request.Question, context);
        }

        var chatService = llmRouter.Resolve(request.Mode);
        var answer = await chatService.CompleteAsync(systemPrompt, userMessage, ct);

        // 检测幻觉
        var isHallucination = await helper.DetectHallucinationAsync(answer, context, request, rerankedResults, ct);

        // 缓存非幻觉答案
        if (!isHallucination && string.IsNullOrWhiteSpace(request.EphemeralContext))
            await semanticCache.SetAsync(queryEmbedding, answer, ct);

        // 构建响应
        var confidence = rerankedResults.Count > 0 ? rerankedResults.Max(r => r.Similarity) : 0f;
        return new RagQueryResponse
        {
            Answer = answer,
            IsHallucination = isHallucination,
            Sources = rerankedResults.Select(r => new SourceReference
            {
                DocumentName = r.Chunk.DocumentName,
                ChunkContent = r.Chunk.Content.Length > RagQueryHelper.SourceContentMaxLength
                    ? r.Chunk.Content[..RagQueryHelper.SourceContentMaxLength] + "..."
                    : r.Chunk.Content,
                Similarity = r.Similarity,
                ChunkId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId
            }).ToList(),
            AnswerConfidence = confidence
        };
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

}
