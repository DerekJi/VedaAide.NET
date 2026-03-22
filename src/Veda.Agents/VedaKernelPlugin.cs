using System.ComponentModel;

namespace Veda.Agents;

/// <summary>
/// Semantic Kernel Plugin — 将知识库检索能力封装为 KernelFunction，
/// 供 ChatCompletionAgent 在推理过程中自主调用（Reason-Act-Observe 循环）。
/// </summary>
public sealed class VedaKernelPlugin(IEmbeddingService embeddingService, IVectorStore vectorStore)
{
    [KernelFunction("search_knowledge_base")]
    [Description("Search the VedaAide knowledge base for relevant document chunks based on a natural language query. Returns the most relevant text passages with their source document names.")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("The natural language query to search for relevant information")] string query,
        [Description("Maximum number of results to return (1-10)")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var embedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var chunks    = await vectorStore.SearchAsync(embedding, topK: topK, minSimilarity: 0.3f, ct: cancellationToken);

        if (!chunks.Any())
            return "No relevant documents found in the knowledge base for this query.";

        return string.Join("\n\n---\n\n", chunks.Select((c, i) =>
            $"[Source {i + 1}: {c.Chunk.DocumentName} (similarity: {c.Similarity:P0})]\n{c.Chunk.Content}"));
    }
}
