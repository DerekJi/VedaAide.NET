using FluentAssertions;
using NUnit.Framework;
using Veda.Core;
using Veda.Prompts;

namespace Veda.Core.Tests;

[TestFixture]
public class ContextWindowBuilderTests
{
    private ContextWindowBuilder _sut = null!;

    [SetUp]
    public void SetUp() => _sut = new ContextWindowBuilder();

    private static (DocumentChunk Chunk, float Similarity) MakeCandidate(
        string content, float similarity) =>
        (new DocumentChunk
        {
            DocumentId   = Guid.NewGuid().ToString(),
            DocumentName = "test.txt",
            DocumentType = DocumentType.Other,
            Content      = content,
            ChunkIndex   = 0
        }, similarity);

    [Test]
    public void Build_EmptyCandidates_ShouldReturnEmpty()
    {
        var result = _sut.Build([], maxTokens: 1000);
        result.Should().BeEmpty();
    }

    [Test]
    public void Build_AllFitInBudget_ShouldReturnAll()
    {
        // 3 chunks × 10 chars = 30 chars; budget = 100 tokens × 3 chars/token = 300 chars
        var candidates = new[]
        {
            MakeCandidate("1234567890", 0.9f),
            MakeCandidate("abcdefghij", 0.8f),
            MakeCandidate("ABCDEFGHIJ", 0.7f),
        };

        var result = _sut.Build(candidates, maxTokens: 100);

        result.Should().HaveCount(3);
    }

    [Test]
    public void Build_ExceedsBudget_ShouldStopAtLimit()
    {
        // Each chunk = 30 chars; budget = 10 tokens × 3 chars = 30 chars → only 1 chunk fits
        var candidates = new[]
        {
            MakeCandidate(new string('a', 30), 0.9f),
            MakeCandidate(new string('b', 30), 0.8f),
        };

        var result = _sut.Build(candidates, maxTokens: 10);

        result.Should().HaveCount(1);
        result[0].Content.Should().StartWith("a");
    }

    [Test]
    public void Build_SortsBySimilarity_ShouldSelectHighestFirst()
    {
        // budget fits only 1 chunk (30 chars each, budget = 10 tokens = 30 chars)
        var candidates = new[]
        {
            MakeCandidate(new string('L', 30), 0.5f),   // low
            MakeCandidate(new string('H', 30), 0.95f),  // high
            MakeCandidate(new string('M', 30), 0.7f),   // medium
        };

        var result = _sut.Build(candidates, maxTokens: 10);

        result.Should().HaveCount(1);
        result[0].Content.Should().StartWith("H");
    }

    [Test]
    public void Build_SingleLargeChunk_ExceedsBudget_ShouldReturnEmpty()
    {
        // chunk = 1000 chars; budget = 1 token × 3 chars = 3 chars → nothing fits
        var candidates = new[]
        {
            MakeCandidate(new string('x', 1000), 1.0f),
        };

        var result = _sut.Build(candidates, maxTokens: 1);

        result.Should().BeEmpty();
    }

    [Test]
    public void Build_MultipleFitting_ShouldReturnInSimilarityDescendingOrder()
    {
        // large budget; verify ordering by similarity
        var candidates = new[]
        {
            MakeCandidate("low",    0.3f),
            MakeCandidate("high",   0.9f),
            MakeCandidate("medium", 0.6f),
        };

        var result = _sut.Build(candidates, maxTokens: 10000);

        result.Select(c => c.Content)
              .Should().ContainInOrder("high", "medium", "low");
    }
}
