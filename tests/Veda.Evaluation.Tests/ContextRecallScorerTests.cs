using FluentAssertions;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Evaluation.Scorers;

namespace Veda.Evaluation.Tests;

[TestFixture]
public class ContextRecallScorerTests
{
    private Mock<IEmbeddingService> _embeddingService = null!;
    private ContextRecallScorer _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _embeddingService = new Mock<IEmbeddingService>();
        _sut = new ContextRecallScorer(_embeddingService.Object);
    }

    [Test]
    public async Task ScoreAsync_EmptySources_ShouldReturnZero()
    {
        var score = await _sut.ScoreAsync("Expected answer.", []);

        score.Should().Be(0f);
        _embeddingService.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ScoreAsync_PerfectMatch_ShouldReturnOne()
    {
        var embedding = new float[] { 1f, 0f };
        _embeddingService
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);
        _embeddingService
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { embedding });

        var sources = new List<SourceReference>
        {
            new() { DocumentName = "doc", ChunkContent = "matching content", Similarity = 0.9f },
        };

        var score = await _sut.ScoreAsync("Expected answer.", sources);

        score.Should().BeApproximately(1f, 0.001f);
    }

    [Test]
    public async Task ScoreAsync_NoMatchingContext_ShouldReturnZero()
    {
        _embeddingService
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 1f, 0f });
        _embeddingService
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[] { 0f, 1f } });

        var sources = new List<SourceReference>
        {
            new() { DocumentName = "doc", ChunkContent = "unrelated content", Similarity = 0.5f },
        };

        var score = await _sut.ScoreAsync("Expected answer.", sources);

        score.Should().BeApproximately(0f, 0.001f);
    }

    [Test]
    public async Task ScoreAsync_AlwaysClampsBetweenZeroAndOne()
    {
        var embedding = new float[] { 1f, 0f };
        _embeddingService
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);
        _embeddingService
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { embedding });

        var sources = new List<SourceReference>
        {
            new() { DocumentName = "doc", ChunkContent = "content", Similarity = 0.8f },
        };

        var score = await _sut.ScoreAsync("answer", sources);

        score.Should().BeInRange(0f, 1f);
    }
}
