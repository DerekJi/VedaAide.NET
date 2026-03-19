using FluentAssertions;
using NUnit.Framework;

namespace Veda.Core.Tests;

[TestFixture]
public class VectorMathTests
{
    [Test]
    public void CosineSimilarity_IdenticalVectors_ShouldReturnOne()
    {
        float[] v = [1f, 2f, 3f];
        var result = VectorMath.CosineSimilarity(v, v);
        result.Should().BeApproximately(1f, precision: 1e-5f);
    }

    [Test]
    public void CosineSimilarity_DifferentLengths_ShouldReturnZero()
    {
        float[] a = [1f, 2f];
        float[] b = [1f, 2f, 3f];
        var result = VectorMath.CosineSimilarity(a, b);
        result.Should().Be(0f);
    }

    [Test]
    public void CosineSimilarity_ZeroVector_ShouldReturnZero()
    {
        float[] zero = [0f, 0f, 0f];
        float[] v = [1f, 2f, 3f];
        var result = VectorMath.CosineSimilarity(zero, v);
        result.Should().Be(0f);
    }

    [Test]
    public void CosineSimilarity_OrthogonalVectors_ShouldReturnZero()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];
        var result = VectorMath.CosineSimilarity(a, b);
        result.Should().BeApproximately(0f, precision: 1e-5f);
    }

    [Test]
    public void CosineSimilarity_OppositeDirectionVectors_ShouldReturnNegativeOne()
    {
        float[] a = [1f, 0f];
        float[] b = [-1f, 0f];
        var result = VectorMath.CosineSimilarity(a, b);
        result.Should().BeApproximately(-1f, precision: 1e-5f);
    }

    [Test]
    public void CosineSimilarity_EmptyVectors_ShouldReturnZero()
    {
        float[] a = [];
        float[] b = [];
        // 0-length vectors: dot=0, norms=0 → denom < epsilon → returns 0
        var result = VectorMath.CosineSimilarity(a, b);
        result.Should().Be(0f);
    }
}
