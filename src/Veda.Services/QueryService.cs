namespace Veda.Services;

/// <summary>
/// RAG 问答查询服务（SRP：只负责检索 + 生成流程）。
/// 依赖：IEmbeddingService、IVectorStore、IChatService。
/// </summary>
public sealed class QueryService(
    IEmbeddingService embeddingService,
    IVectorStore vectorStore,
    IChatService chatService,
    ILogger<QueryService> logger) : IQueryService
{
    /// <summary>引用来源的最大展示字符数，超出部分截断并追加省略号。</summary>
    internal const int SourceContentMaxLength = 200;

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

        var results = await vectorStore.SearchAsync(
            queryEmbedding,
            topK: request.TopK,
            minSimilarity: request.MinSimilarity,
            filterType: request.FilterDocumentType,
            ct: ct);

        if (results.Count == 0)
            return new RagQueryResponse
            {
                Answer = "I don't have enough information in the provided documents.",
                AnswerConfidence = 0f
            };

        var userMessage = $"Context:\n{BuildContext(results)}\n\nQuestion: {request.Question}";
        var answer = await chatService.CompleteAsync(SystemPrompt, userMessage, ct);

        return new RagQueryResponse
        {
            Answer = answer,
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
