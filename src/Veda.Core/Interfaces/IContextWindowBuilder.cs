namespace Veda.Core.Interfaces;

/// <summary>
/// 根据 Token 预算从候选文档块列表中选取最优上下文集合。
/// </summary>
public interface IContextWindowBuilder
{
    /// <summary>
    /// 从按相似度排序的候选块中，选取不超过 <paramref name="maxTokens"/> Token 的块集合。
    /// </summary>
    IReadOnlyList<DocumentChunk> Build(
        IReadOnlyList<(DocumentChunk Chunk, float Similarity)> candidates,
        int maxTokens = 3000);
}
