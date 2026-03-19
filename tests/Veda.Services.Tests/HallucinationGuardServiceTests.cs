using FluentAssertions;
using Moq;
using NUnit.Framework;
using Veda.Core.Interfaces;
using Veda.Services;

namespace Veda.Services.Tests;

[TestFixture]
public class HallucinationGuardServiceTests
{
    private Mock<IChatService> _chatService = null!;
    private HallucinationGuardService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _chatService = new Mock<IChatService>();
        _sut = new HallucinationGuardService(_chatService.Object);
    }

    [Test]
    public async Task VerifyAsync_LlmRespondsTrue_ShouldReturnTrue()
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");

        var result = await _sut.VerifyAsync("The answer.", "The context.");

        result.Should().BeTrue();
    }

    [Test]
    public async Task VerifyAsync_LlmRespondsFalse_ShouldReturnFalse()
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("false");

        var result = await _sut.VerifyAsync("Fabricated answer.", "Context.");

        result.Should().BeFalse();
    }

    [TestCase("True")]
    [TestCase("TRUE")]
    [TestCase("true\n")]
    [TestCase("  true  ")]
    public async Task VerifyAsync_LlmRespondsTrueWithVariousFormats_ShouldReturnTrue(string llmResponse)
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var result = await _sut.VerifyAsync("Answer.", "Context.");

        result.Should().BeTrue();
    }

    [TestCase("false")]
    [TestCase("False")]
    [TestCase("I cannot verify this.")]
    [TestCase("")]
    public async Task VerifyAsync_LlmRespondsNonTrue_ShouldReturnFalse(string llmResponse)
    {
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);

        var result = await _sut.VerifyAsync("Answer.", "Context.");

        result.Should().BeFalse();
    }

    [Test]
    public async Task VerifyAsync_ShouldIncludeAnswerAndContextInLlmMessage()
    {
        // Arrange
        string? capturedUserMessage = null;
        _chatService
            .Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, msg, _) => capturedUserMessage = msg)
            .ReturnsAsync("true");

        // Act
        await _sut.VerifyAsync("My answer.", "My context.");

        // Assert
        capturedUserMessage.Should().Contain("My answer.");
        capturedUserMessage.Should().Contain("My context.");
    }
}
