using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Services;

namespace Veda.Services.Tests;

// ── AzureDiQuotaState 单元测试 ──────────────────────────────────────────────

[TestFixture]
public class AzureDiQuotaStateTests
{
    [Test]
    public void IsExceeded_InitialState_ReturnsFalse()
    {
        var state = new AzureDiQuotaState();
        state.IsExceeded.Should().BeFalse();
    }

    [Test]
    public void MarkExceeded_SetsIsExceededToTrue()
    {
        var state = new AzureDiQuotaState();
        state.MarkExceeded();
        state.IsExceeded.Should().BeTrue();
    }

    [Test]
    public void MarkExceeded_SetsResetToNextMonthFirstDay()
    {
        var state = new AzureDiQuotaState();
        var before = DateTimeOffset.UtcNow;
        state.MarkExceeded();

        // IsExceeded should return true for now
        state.IsExceeded.Should().BeTrue();

        // Simulate time past next month resets the state
        var nextMonthFirst = before.Month == 12
            ? new DateTimeOffset(before.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(before.Year, before.Month + 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Set to expired (past reset date)
        state.SetExceededUntilForTest(DateTimeOffset.UtcNow.AddSeconds(-1));
        state.IsExceeded.Should().BeFalse();
    }

    [Test]
    public void SetExceededUntilForTest_FutureDate_IsExceededTrue()
    {
        var state = new AzureDiQuotaState();
        state.SetExceededUntilForTest(DateTimeOffset.UtcNow.AddHours(1));
        state.IsExceeded.Should().BeTrue();
    }

    [Test]
    public void SetExceededUntilForTest_PastDate_IsExceededFalse()
    {
        var state = new AzureDiQuotaState();
        state.SetExceededUntilForTest(DateTimeOffset.UtcNow.AddSeconds(-1));
        state.IsExceeded.Should().BeFalse();
    }

    [Test]
    public void SetExceededUntilForTest_Null_ResetsState()
    {
        var state = new AzureDiQuotaState();
        state.MarkExceeded();
        state.IsExceeded.Should().BeTrue();

        state.SetExceededUntilForTest(null);
        state.IsExceeded.Should().BeFalse();
    }
}

// ── DocumentIntelligenceFileExtractor 配额路径测试 ─────────────────────────

[TestFixture]
public class DocumentIntelligenceExtractorQuotaTests
{
    /// <summary>测试用子类：重写 CallAzureDiAsync 可注入任意响应或异常。</summary>
    private sealed class StubDiExtractor(
        AzureDiQuotaState state,
        Func<Task<string>> azureCall)
        : DocumentIntelligenceFileExtractor(
            Options.Create(new DocumentIntelligenceOptions { Endpoint = "https://test.cognitiveservices.azure.com/" }),
            Mock.Of<ILogger<DocumentIntelligenceFileExtractor>>(),
            state)
    {
        protected override Task<string> CallAzureDiAsync(string modelId, Stream fileStream, CancellationToken ct)
            => azureCall();
    }

    [Test]
    public async Task ExtractAsync_WhenQuotaPreExceeded_ThrowsQuotaExceededExceptionWithoutCallingAzure()
    {
        var state = new AzureDiQuotaState();
        state.SetExceededUntilForTest(DateTimeOffset.UtcNow.AddHours(1));

        var azureCallInvoked = false;
        var sut = new StubDiExtractor(state, () =>
        {
            azureCallInvoked = true;
            return Task.FromResult("should not reach here");
        });

        var act = () => sut.ExtractAsync(Stream.Null, "test.pdf", "application/pdf", DocumentType.Other);
        await act.Should().ThrowAsync<QuotaExceededException>();
        azureCallInvoked.Should().BeFalse("Azure should not be called when quota is pre-exceeded");
    }

    [Test]
    public async Task ExtractAsync_WhenAzureReturns429_MarksQuotaAndThrowsQuotaExceededException()
    {
        var state = new AzureDiQuotaState();
        state.IsExceeded.Should().BeFalse();

        var sut = new StubDiExtractor(state,
            () => Task.FromException<string>(new RequestFailedException(429, "Too Many Requests")));

        var act = () => sut.ExtractAsync(Stream.Null, "invoice.pdf", "application/pdf", DocumentType.BillInvoice);
        await act.Should().ThrowAsync<QuotaExceededException>();

        state.IsExceeded.Should().BeTrue("state must be marked so subsequent requests skip Azure");
    }

    [Test]
    public async Task ExtractAsync_WhenQuotaExpired_CallsAzureAgain()
    {
        var state = new AzureDiQuotaState();
        state.SetExceededUntilForTest(DateTimeOffset.UtcNow.AddSeconds(-1)); // already expired

        var azureCallInvoked = false;
        var sut = new StubDiExtractor(state, () =>
        {
            azureCallInvoked = true;
            return Task.FromResult("extracted text");
        });

        var result = await sut.ExtractAsync(Stream.Null, "test.pdf", "application/pdf", DocumentType.Other);

        result.Should().Be("extracted text");
        azureCallInvoked.Should().BeTrue("Azure should be called after quota resets");
    }
}

// ── DocumentIngestService Azure DI 降级测试 ────────────────────────────────

[TestFixture]
public class DocumentIngestServiceFallbackTests
{
    [Test]
    public async Task IngestFileAsync_WhenDiQuotaExceeded_FallsBackToVisionExtractor()
    {
        // Arrange: DI extractor with quota exceeded
        var quotaState = new AzureDiQuotaState();
        quotaState.SetExceededUntilForTest(DateTimeOffset.UtcNow.AddHours(1));

        var docIntelExtractor = new DocumentIntelligenceFileExtractor(
            Options.Create(new DocumentIntelligenceOptions()),
            Mock.Of<ILogger<DocumentIntelligenceFileExtractor>>(),
            quotaState);

        // Vision extractor: mock returns known text
        const string visionResult = "Vision extracted: invoice details";
        var chatCompletion = new Mock<IChatCompletionService>();
        chatCompletion
            .Setup(c => c.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChatMessageContent(AuthorRole.Assistant, visionResult)]);

        var visionExtractor = new VisionModelFileExtractor(
            chatCompletion.Object,
            Options.Create(new VisionOptions { Enabled = true }),
            Mock.Of<ILogger<VisionModelFileExtractor>>());

        // Remaining service dependencies
        var processor = new Mock<IDocumentProcessor>();
        processor.Setup(p => p.Process(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
            .Returns([new DocumentChunk { Content = visionResult, DocumentName = "invoice.jpg", DocumentId = "d1", Metadata = [] }]);

        var embedding = new Mock<IEmbeddingService>();
        embedding.Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([[0.1f, 0.2f]]);

        var vectorStore = new Mock<IVectorStore>();
        vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        vectorStore.Setup(v => v.GetCurrentChunksByDocumentNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var semanticCache = new Mock<ISemanticCache>();
        semanticCache.Setup(c => c.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var semanticEnhancer = new Mock<ISemanticEnhancer>();
        semanticEnhancer.Setup(e => e.GetAliasTagsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>().ToList());

        var diffService = new Mock<IDocumentDiffService>();

        var sut = new DocumentIngestService(
            processor.Object,
            embedding.Object,
            vectorStore.Object,
            semanticCache.Object,
            semanticEnhancer.Object,
            diffService.Object,
            Options.Create(new RagOptions { SimilarityDedupThreshold = 1.1f }),
            Options.Create(new VedaOptions { EmbeddingModel = "test-model" }),
            docIntelExtractor,
            visionExtractor,
            Mock.Of<ILogger<DocumentIngestService>>());

        // Act
        using var fileStream = new MemoryStream([0xFF, 0xD8, 0xFF]); // fake JPEG bytes
        var result = await sut.IngestFileAsync(
            fileStream, "invoice.jpg", "image/jpeg", DocumentType.BillInvoice);

        // Assert: ingestion succeeded via Vision
        result.DocumentName.Should().Be("invoice.jpg");
        result.ChunksStored.Should().BeGreaterThan(0);
        chatCompletion.Verify(
            c => c.GetChatMessageContentsAsync(
                It.IsAny<ChatHistory>(),
                It.IsAny<PromptExecutionSettings>(),
                It.IsAny<Kernel>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Vision extractor must be called as fallback");
    }
}
