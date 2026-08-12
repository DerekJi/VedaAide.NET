
using Veda.Core.Extensions;
namespace Veda.Services;

/// <summary>
/// Streaming Q&A service: yields sources first, then yields the LLM output token by token,
/// and finally yields done (with the hallucination flag). Reuses RagQueryHelper for code reuse,
/// with the core logic split into small single-responsibility methods.
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

        // Try to return the cached answer
        var cachedAnswer = await GetCachedAnswerAsync(request, queryEmbedding, ct);
        if (cachedAnswer is not null)
        {
            yield return new RagStreamChunk { Type = "sources", Sources = [] };
            yield return new RagStreamChunk { Type = "token", Token = cachedAnswer };
            yield return new RagStreamChunk { Type = "done", AnswerConfidence = 1f, IsHallucination = false };
            yield break;
        }

        // Retrieve and rerank candidates
        var candidates = await ragQueryHelper.RetrieveCandidatesAsync(expandedQuestion, queryEmbedding, request, ct);
        var results = await ragQueryHelper.RerankAndBoostAsync(candidates, request.Question, request.TopK, request.UserId, ct);

        // Finish early when there are no results and no ephemeral context
        if (results.Count == 0 && string.IsNullOrWhiteSpace(request.EphemeralContext))
        {
            yield return new RagStreamChunk { Type = "sources", Sources = [] };
            yield return new RagStreamChunk { Type = "token", Token = "I don't have enough information in the provided documents." };
            yield return new RagStreamChunk { Type = "done", AnswerConfidence = 0f, IsHallucination = false };
            yield break;
        }

        // Send the source list to the frontend
        yield return BuildSourcesChunk(results);

        // Generate the streaming answer
        var (answer, isHallucination) = await GenerateStreamAnswerAsync(
            expandedQuestion, results, request, ct);

        // Cache the non-hallucinated answer
        if (!isHallucination && string.IsNullOrWhiteSpace(request.EphemeralContext))
            await semanticCache.SetAsync(queryEmbedding, answer, ct);

        // Send the done signal
        var confidence = results.Count > 0 ? results.Max(r => r.Similarity) : 0f;
        yield return new RagStreamChunk
        {
            Type = "done",
            AnswerConfidence = confidence,
            IsHallucination = isHallucination
        };
    }

    /// <summary>Gets the answer from the cache, if any.</summary>
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
    /// Dynamically builds the System Prompt: prefers loading the "rag-system" template from the database,
    /// falling back to hard-coded default content. The template supports the {today} placeholder,
    /// and the language-rule instruction is adjusted automatically based on the question language.
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

    /// <summary>Builds the sources-list chunk.</summary>
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

    /// <summary>Generates the streaming answer and detects hallucinations.</summary>
    private async Task<(string Answer, bool IsHallucination)> GenerateStreamAnswerAsync(
        string expandedQuestion,
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> results,
        RagQueryRequest request,
        CancellationToken ct)
    {
        // Build the context
        var contextChunks = contextWindowBuilder.Build(results);
        var context = ragQueryHelper.BuildContext(contextChunks, request.EphemeralContext);

        // Generate the answer
        var systemPrompt = await BuildSystemPromptAsync(request.Question, ct);
        var userMessage = chainOfThought.Enhance(request.Question, context);

        var chatService = llmRouter.Resolve(request.Mode);
        var fullAnswer = new System.Text.StringBuilder();
        await foreach (var token in chatService.CompleteStreamAsync(systemPrompt, userMessage, ct))
        {
            fullAnswer.Append(token);
        }

        var answer = fullAnswer.ToString();

        // Detect hallucinations
        var isHallucination = await ragQueryHelper.DetectHallucinationAsync(
            answer, context, request, results, ct);

        return (answer, isHallucination);
    }
}
