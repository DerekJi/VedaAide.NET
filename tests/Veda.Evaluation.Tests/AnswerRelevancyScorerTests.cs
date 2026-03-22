using FluentAssertions;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Evaluation.Scorers;

namespace Veda.Evaluation.Tests;

[TestFixture]
public class AnswerRelevancyScorerTests
{
    private Mock<IEmbeddingService> _embeddingService = null!;
    private AnswerRelevancyScorer _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _embeddingService = new Mock<IEmbeddingService>();
        _sut = new AnswerRelevancyScorer(_embeddingService.Object);
    }

    [Test]
    public async Task ScoreAsync_IdenticalEmbeddings_ShouldReturnOne()
    {
        var embedding = new float[] { 1f, 0f, 0f };
        _embeddingService
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embedding);

        var score = await _sut.ScoreAsync("What is the policy?", "The policy is...");

        score.Should().BeApproximately(1f, 0.001f);
    }

    [Test]
    public async Task ScoreAsync_OrthogonalEmbeddings_ShouldReturnZero()
    {
        _embeddingService
            .SetupSequence(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 1f, 0f })
            .ReturnsAsync(new float[] { 0f, 1f });

        var score = await _sut.ScoreAsync("Does not match.", "Completely different.");

        score.Should().BeApproximately(0f, 0.001f);
    }

    [Test]
    public async Task ScoreAsync_AlwaysClampsBetweenZeroAndOne()
    {
        _embeddingService
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 1f, 0f });

        var score = await _sut.ScoreAsync("q", "a");

        score.Should().BeInRange(0f, 1f);
    }
}
