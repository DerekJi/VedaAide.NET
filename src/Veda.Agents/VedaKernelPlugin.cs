using System.ComponentModel;

namespace Veda.Agents;

/// <summary>
/// Knowledge-base tool for AIAgent.
/// Wraps knowledge-base retrieval as a function that agents can invoke autonomously 
/// during reasoning (Reason-Act-Observe loop).
/// Updated for Microsoft Agent Framework - no KernelFunction attribute needed.
/// </summary>
public class VedaKnowledgeBaseTool(IEmbeddingService embeddingService, IVectorStore vectorStore)
{
    /// <summary>
    /// Search the VedaAide knowledge base for relevant document chunks.
    /// </summary>
    [Description("Search the VedaAide knowledge base for relevant document chunks based on a natural language query. Returns the most relevant text passages with their source document names.")]
    public async Task<string> SearchKnowledgeBase(
        [Description("The natural language query to search for relevant information")] string query,
        [Description("Maximum number of results to return (1-10), default is 5")] int topK = 5,
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

/// <summary>
/// Legacy alias for backward compatibility.
/// New code should use VedaKnowledgeBaseTool instead.
/// </summary>
[Obsolete("Use VedaKnowledgeBaseTool instead")]
public sealed class VedaKernelPlugin(IEmbeddingService embeddingService, IVectorStore vectorStore)
    : VedaKnowledgeBaseTool(embeddingService, vectorStore)
{
}
