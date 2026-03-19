namespace Veda.Core;

/// <summary>
/// 向量空间数学工具。
/// 职责：纯数学运算，与存储实现完全解耦（SRP）。
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// 计算两个向量的余弦相似度。返回值范围 [-1, 1]；维度不一致时返回 0。
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) return 0f;

        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < float.Epsilon ? 0f : dot / denom;
    }
}
