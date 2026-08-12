using Veda.Core.Options;
namespace Veda.Core.Interfaces;

/// <summary>
/// Hybrid retrieval service that fuses two channels.
/// Runs the vector channel and the keyword channel concurrently, and returns fused, ranked results via an RRF or weighted merge strategy.
/// </summary>
public interface IHybridRetriever
{
    Task<IReadOnlyList<(DocumentChunk Chunk, float Score)>> RetrieveAsync(
        string query,
        float[] queryEmbedding,
        int topK,
        HybridRetrievalOptions options,
        KnowledgeScope? scope = null,
        float minSimilarity = 0f,
        DocumentType? filterType = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        CancellationToken ct = default);
}
