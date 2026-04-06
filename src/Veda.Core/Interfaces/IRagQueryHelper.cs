namespace Veda.Core.Interfaces;

/// <summary>
/// RAG 查询的共享辅助服务接口：提供检索、排名、上下文构建等公共逻辑。
/// </summary>
public interface IRagQueryHelper
{
    /// <summary>检索候选：根据配置选择混合检索或向量检索。</summary>
    Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> RetrieveCandidatesAsync(
        string expandedQuestion,
        float[] queryEmbedding,
        RagQueryRequest request,
        CancellationToken ct);

    /// <summary>排名与反馈 boost：轻量重排后应用用户反馈 boost。</summary>
    Task<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>> RerankAndBoostAsync(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        string question,
        int topK,
        string? userId,
        CancellationToken ct);

    /// <summary>轻量重排：70% 向量相似度 + 30% 问题关键词覆盖率。</summary>
    IReadOnlyList<(DocumentChunk Chunk, float Similarity)> Rerank(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        string question,
        int topK);

    /// <summary>构建上下文：从 Token 预算裁剪后的文本块列表构建上下文。</summary>
    string BuildContext(IReadOnlyList<DocumentChunk> chunks, string? ephemeralContext = null);

    /// <summary>检测答案是否为幻觉。</summary>
    Task<bool> DetectHallucinationAsync(
        string answer,
        string context,
        RagQueryRequest request,
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> results,
        CancellationToken ct);
}
