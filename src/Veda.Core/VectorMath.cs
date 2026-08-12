namespace Veda.Core;

/// <summary>
/// Vector space math utilities.
/// Responsibility: pure math operations, fully decoupled from storage implementations (SRP).
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Computes the cosine similarity of two vectors. The return value ranges over [-1, 1]; returns 0 when the dimensions do not match.
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
