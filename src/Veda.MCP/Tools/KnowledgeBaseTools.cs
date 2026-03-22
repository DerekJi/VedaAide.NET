namespace Veda.MCP.Tools;

/// <summary>
/// MCP 知识库检索工具。提供向量搜索和文档列表两个工具。
/// 通过 DI 构造函数注入所需服务。
/// </summary>
[McpServerToolType]
public sealed class KnowledgeBaseTools(IEmbeddingService embeddingService, IVectorStore vectorStore)
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    [McpServerTool(Name = "search_knowledge_base")]
    [Description("Search the VedaAide knowledge base for relevant document chunks. Returns JSON array of matching chunks with similarity scores.")]
    public async Task<string> SearchKnowledgeBase(
        [Description("The search query text")] string query,
        [Description("Maximum number of results to return (1-20)")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        topK = Math.Clamp(topK, 1, 20);

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var results = await vectorStore.SearchAsync(queryEmbedding, topK, ct: cancellationToken);

        var output = results.Select(r => new
        {
            documentName = r.Chunk.DocumentName,
            content = r.Chunk.Content.Length > 500
                ? r.Chunk.Content[..500] + "..."
                : r.Chunk.Content,
            similarity = Math.Round(r.Similarity, 4),
            documentType = r.Chunk.DocumentType.ToString()
        });

        return JsonSerializer.Serialize(output, SerializerOptions);
    }

    [McpServerTool(Name = "list_documents")]
    [Description("List all documents currently stored in the VedaAide knowledge base.")]
    public async Task<string> ListDocuments(CancellationToken cancellationToken = default)
    {
        var results = await vectorStore.SearchAsync(
            queryEmbedding: new float[384], // zero vector — retrieves all with no ranking
            topK: 200,
            minSimilarity: 0f,
            ct: cancellationToken);

        var documents = results
            .GroupBy(r => new { r.Chunk.DocumentId, r.Chunk.DocumentName, r.Chunk.DocumentType })
            .Select(g => new
            {
                documentId = g.Key.DocumentId,
                documentName = g.Key.DocumentName,
                documentType = g.Key.DocumentType.ToString(),
                chunkCount = g.Count()
            })
            .OrderBy(x => x.documentName)
            .ToList();

        return JsonSerializer.Serialize(documents, SerializerOptions);
    }
}
