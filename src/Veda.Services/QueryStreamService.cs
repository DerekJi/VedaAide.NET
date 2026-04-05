
using Veda.Core.Extensions;
namespace Veda.Services;

/// <summary>
/// 流式问答服务：先 yield sources，再逐 token yield LLM 输出，最后 yield done（含幻觉标志）。
/// 使用 RagQueryHelper 实现代码复用，核心逻辑拆分为单一职责的小方法。
/// </summary>
public sealed class QueryStreamService(
    IEmbeddingService embeddingService,
    ISemanticCache semanticCache,
    ILogger logger,
    IContextWindowBuilder contextWindowBuilder,
    IChainOfThoughtStrategy chainOfThought,
    ILlmRouter llmRouter,
    IPromptTemplateRepository promptTemplateRepository,
    IRagQueryHelper ragQueryHelper) : IQueryStreamService
{

    public async IAsyncEnumerable<RagStreamChunk> QueryStreamAsync(
        RagQueryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);
        logger.LogInformation("RAG stream query: {Question}", request.Question);

        var expandedQuestion = await embeddingService.ExpandQueryAsync(request.Question, ct);
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(expandedQuestion, ct);

        // 尝试返回缓存答案
        var cachedAnswer = await GetCachedAnswerAsync(request, queryEmbedding, ct);
        if (cachedAnswer is not null)
        {
            yield return new RagStreamChunk { Type = "sources", Sources = [] };
            yield return new RagStreamChunk { Type = "token", Token = cachedAnswer };
            yield return new RagStreamChunk { Type = "done", AnswerConfidence = 1f, IsHallucination = false };
            yield break;
        }

        // 检索并排名候选
        var candidates = await ragQueryHelper.RetrieveCandidatesAsync(expandedQuestion, queryEmbedding, request, ct);
        var results = await ragQueryHelper.RerankAndBoostAsync(candidates, request.Question, request.TopK, request.UserId, ct);

        // 无结果且无临时上下文时提前结束
        if (results.Count == 0 && string.IsNullOrWhiteSpace(request.EphemeralContext))
        {
            yield return new RagStreamChunk { Type = "sources", Sources = [] };
            yield return new RagStreamChunk { Type = "token", Token = "I don't have enough information in the provided documents." };
            yield return new RagStreamChunk { Type = "done", AnswerConfidence = 0f, IsHallucination = false };
            yield break;
        }

        // 发送源列表给前端
        yield return BuildSourcesChunk(results);

        // 生成流式答案
        var (answer, isHallucination) = await GenerateStreamAnswerAsync(
            expandedQuestion, results, request, ct);

        // 缓存非幻觉答案
        if (!isHallucination && string.IsNullOrWhiteSpace(request.EphemeralContext))
            await semanticCache.SetAsync(queryEmbedding, answer, ct);

        // 发送完成信号
        var confidence = results.Count > 0 ? results.Max(r => r.Similarity) : 0f;
        yield return new RagStreamChunk
        {
            Type = "done",
            AnswerConfidence = confidence,
            IsHallucination = isHallucination
        };
    }

    /// <summary>从缓存中获取答案（如果有）。</summary>
    private async Task<string?> GetCachedAnswerAsync(
        RagQueryRequest request,
        float[] queryEmbedding,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.EphemeralContext))
            return null;

        var cachedAnswer = await semanticCache.GetAsync(queryEmbedding, ct);
        if (cachedAnswer is not null)
            logger.LogInformation("Semantic cache hit for: {Question}", request.Question);

        return cachedAnswer;
    }

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

    /// <summary>构建源列表 chunk。</summary>
    private static RagStreamChunk BuildSourcesChunk(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> results)
    {
        return new RagStreamChunk
        {
            Type = "sources",
            Sources = results.Select(r => new SourceReference
            {
                DocumentName = r.Chunk.DocumentName,
                ChunkContent = r.Chunk.Content.Length > RagQueryHelper.SourceContentMaxLength
                    ? r.Chunk.Content[..RagQueryHelper.SourceContentMaxLength] + "..."
                    : r.Chunk.Content,
                Similarity = r.Similarity,
                ChunkId = r.Chunk.Id,
                DocumentId = r.Chunk.DocumentId
            }).ToList()
        };
    }

    /// <summary>生成流式答案并检测幻觉。</summary>
    private async Task<(string Answer, bool IsHallucination)> GenerateStreamAnswerAsync(
        string expandedQuestion,
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> results,
        RagQueryRequest request,
        CancellationToken ct)
    {
        // 构建上下文
        var contextChunks = contextWindowBuilder.Build(results);
        var context = ragQueryHelper.BuildContext(contextChunks, request.EphemeralContext);

        // 生成答案
        var systemPrompt = await BuildSystemPromptAsync(request.Question, ct);
        var userMessage = chainOfThought.Enhance(request.Question, context);

        var chatService = llmRouter.Resolve(request.Mode);
        var fullAnswer = new System.Text.StringBuilder();
        await foreach (var token in chatService.CompleteStreamAsync(systemPrompt, userMessage, ct))
        {
            fullAnswer.Append(token);
        }

        var answer = fullAnswer.ToString();

        // 检测幻觉
        var isHallucination = await ragQueryHelper.DetectHallucinationAsync(
            answer, context, request, results, ct);

        return (answer, isHallucination);
    }
}
