
using Veda.Core.Extensions;
namespace Veda.Services;

/// <summary>
/// RAG synchronous Q&A query service (SRP: only responsible for retrieval + generating the complete answer).
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
    /// Dynamically builds the System Prompt: prefers loading the "rag-system" template from the database,
    /// falling back to hard-coded default content. The template supports the {today} placeholder,
    /// and the language-rule instruction is adjusted automatically based on the question language.
    /// </summary>
    internal async Task<string> BuildSystemPromptAsync(string question, CancellationToken ct)
    {
        var today = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        var template = await promptTemplateRepository.GetLatestAsync("rag-system", ct);
        if (template is not null)
            return template.Content.Replace("{today}", today, StringComparison.Ordinal);

        var langRule = question.IsChinese()
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

    public async Task<RagQueryResponse> QueryAsync(RagQueryRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);
        logger.LogInformation("RAG query: {Question}", request.Question);

        // Semantic enhancement: query expansion
        var expandedQuestion = await semanticEnhancer.ExpandQueryAsync(request.Question, ct);
        var embeddingVector = await embeddingService.GenerateEmbeddingAsync(expandedQuestion, ct);
        logger.LogInformation("Generated embeddingVector with length: {Length}", embeddingVector.Length);

        // Check the semantic cache
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(expandedQuestion, ct);
        var cachedAnswer = string.IsNullOrWhiteSpace(request.EphemeralContext)
            ? await semanticCache.GetAsync(queryEmbedding, ct)
            : null;
        if (cachedAnswer is not null)
        {
            logger.LogInformation("Semantic cache hit for: {Question}", request.Question);
            return new RagQueryResponse { Answer = cachedAnswer, AnswerConfidence = 1f, IsHallucination = false };
        }

        // Retrieve and rerank
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

        // Build the context and generate the answer
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

        // Detect hallucinations
        var isHallucination = await helper.DetectHallucinationAsync(answer, context, request, rerankedResults, ct);

        // Cache the non-hallucinated answer
        if (!isHallucination && string.IsNullOrWhiteSpace(request.EphemeralContext))
            await semanticCache.SetAsync(queryEmbedding, answer, ct);

        // Build the response
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

    /// <summary>Builds the structured-output Prompt that forces the LLM to return a specific JSON format.</summary>
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
