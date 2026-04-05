using System.Security.Cryptography;
using System.Text;
using Veda.Core.Interfaces;

namespace Veda.Services.Tests.Integration;

/// <summary>
/// Deterministic embedding stub for integration tests.
/// Converts input text to a SHA-256-derived unit vector — no Ollama or Azure required.
/// Same text → identical vector (cosine = 1.0 with itself).
/// Different texts → distinct vectors, enabling vector search to distinguish documents.
/// </summary>
internal sealed class FakeEmbeddingService : IEmbeddingService
{
    // 384 dimensions — arbitrary choice that works with SqliteVSS (stores raw float blobs).
    private const int Dimensions = 384;

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
        => Task.FromResult(ToUnitVector(text));

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        IReadOnlyList<float[]> result = texts.Select(ToUnitVector).ToList();
        return Task.FromResult(result);
    }

    public Task<string> ExpandQueryAsync(string text, CancellationToken ct = default)
        => Task.FromResult(text);  // For testing, no expansion needed

    private static float[] ToUnitVector(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var vec  = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++)
            vec[i] = (float)(hash[i % hash.Length] - 128) / 128f;

        var norm = MathF.Sqrt(vec.Sum(v => v * v));
        if (norm > 0f)
            for (var i = 0; i < Dimensions; i++) vec[i] /= norm;

        return vec;
    }
}
