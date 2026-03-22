using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Veda.Core.Interfaces;
using Veda.Evaluation.Scorers;

namespace Veda.Evaluation.Tests;

[TestFixture]
public class FaithfulnessScorerTests
{
    private Mock<IChatService> _chatService = null!;
    private FaithfulnessScorer _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _chatService = new Mock<IChatService>();
        _sut = new FaithfulnessScorer(_chatService.Object, NullLogger<FaithfulnessScorer>.Instance);
    }

    [Test]
    public async Task ScoreAsync_LlmReturnsOne_ShouldReturnOnePointZero()
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.0");

        var score = await _sut.ScoreAsync("The answer.", "The supporting context.");

        score.Should().Be(1.0f);
    }

    [Test]
    public async Task ScoreAsync_LlmReturnsZero_ShouldReturnZero()
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0.0");

        var score = await _sut.ScoreAsync("Fabricated answer.", "Unrelated context.");

        score.Should().Be(0.0f);
    }

    [Test]
    public async Task ScoreAsync_LlmReturnsPartialScore_ShouldClampToRange()
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0.65");

        var score = await _sut.ScoreAsync("Partial answer.", "Context.");

        score.Should().BeApproximately(0.65f, 0.001f);
    }

    [Test]
    public async Task ScoreAsync_LlmReturnsOutOfRangeHigh_ShouldClampToOne()
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.5");

        var score = await _sut.ScoreAsync("Answer.", "Context.");

        score.Should().Be(1.0f);
    }

    [Test]
    public async Task ScoreAsync_LlmReturnsGarbage_ShouldReturnZero()
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("not a number");

        var score = await _sut.ScoreAsync("Answer.", "Context.");

        score.Should().Be(0.0f);
    }

    [Test]
    public async Task ScoreAsync_LlmThrows_ShouldReturnZero()
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("LLM unavailable"));

        var score = await _sut.ScoreAsync("Answer.", "Context.");

        score.Should().Be(0.0f);
    }
}
