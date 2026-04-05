namespace Veda.Core.Interfaces;

/// <summary>
/// 将文本转换为向量（Embedding）的服务契约。
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default);

    /// <summary>
    /// 扩展查询文本以增强语义。
    /// </summary>
    Task<string> ExpandQueryAsync(string text, CancellationToken ct = default);
}
