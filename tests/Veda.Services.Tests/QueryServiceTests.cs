using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Services;

namespace Veda.Services.Tests;

[TestFixture]
public class QueryServiceTests
{
    private Mock<IEmbeddingService> _embedding = null!;
    private Mock<IVectorStore> _vectorStore = null!;
    private Mock<IChatService> _chatService = null!;
    private Mock<ILogger<QueryService>> _logger = null!;
    private QueryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _embedding = new Mock<IEmbeddingService>();
        _vectorStore = new Mock<IVectorStore>();
        _chatService = new Mock<IChatService>();
        _logger = new Mock<ILogger<QueryService>>();
        _sut = new QueryService(
            _embedding.Object,
            _vectorStore.Object,
            _chatService.Object,
            _logger.Object);
    }

    [Test]
    public async Task QueryAsync_EmptyQuestion_ShouldThrowArgumentException()
    {
        var request = new RagQueryRequest { Question = "  " };
        var act = () => _sut.QueryAsync(request);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task QueryAsync_NoResults_ShouldReturnNoInfoMessage()
    {
        // Arrange
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk, float)>());

        // Act
        var response = await _sut.QueryAsync(new RagQueryRequest { Question = "What is X?" });

        // Assert
        response.Answer.Should().Contain("don't have enough information");
        response.AnswerConfidence.Should().Be(0f);
        _chatService.Verify(c => c.CompleteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task QueryAsync_ResultsFound_ShouldReturnAnswerAndSources()
    {
        // Arrange
        var chunks = new List<(DocumentChunk, float)>
        {
            (MakeChunk("doc1", "relevant content about X"), 0.9f),
            (MakeChunk("doc2", "more info about X"), 0.75f)
        };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("X is a thing.");

        // Act
        var response = await _sut.QueryAsync(new RagQueryRequest { Question = "What is X?" });

        // Assert
        response.Answer.Should().Be("X is a thing.");
        response.Sources.Should().HaveCount(2);
        response.Sources[0].DocumentName.Should().Be("doc1");
        response.Sources[1].DocumentName.Should().Be("doc2");
    }

    [Test]
    public async Task QueryAsync_ResultsFound_ShouldSetConfidenceToMaxSimilarity()
    {
        // Arrange
        var chunks = new List<(DocumentChunk, float)>
        {
            (MakeChunk("doc1", "content"), 0.8f),
            (MakeChunk("doc2", "content"), 0.95f)
        };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some answer.");

        // Act
        var response = await _sut.QueryAsync(new RagQueryRequest { Question = "Question?" });

        // Assert
        response.AnswerConfidence.Should().Be(0.95f);
    }

    [Test]
    public async Task QueryAsync_WithFilterType_ShouldPassFilterToVectorStore()
    {
        // Arrange
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk, float)>());

        var request = new RagQueryRequest { Question = "Q?", FilterDocumentType = DocumentType.BillInvoice };

        // Act
        await _sut.QueryAsync(request);

        // Assert
        _vectorStore.Verify(v => v.SearchAsync(
            It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
            DocumentType.BillInvoice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueryAsync_LongChunkContent_ShouldTruncateSourceAtMaxLength()
    {
        // Arrange: content that exceeds the configured max display length
        var longContent = new string('A', QueryService.SourceContentMaxLength + 100);
        var chunks = new List<(DocumentChunk, float)>
        {
            (MakeChunk("doc1", longContent), 0.9f)
        };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer.");

        // Act
        var response = await _sut.QueryAsync(new RagQueryRequest { Question = "Q?" });

        // Assert: truncated to SourceContentMaxLength chars + "..." (3 chars)
        response.Sources[0].ChunkContent.Should().HaveLength(QueryService.SourceContentMaxLength + 3);
        response.Sources[0].ChunkContent.Should().EndWith("...");
    }

    private static DocumentChunk MakeChunk(string docName, string content) => new()
    {
        Id = Guid.NewGuid().ToString(),
        DocumentId = "doc-id",
        DocumentName = docName,
        DocumentType = DocumentType.Other,
        Content = content,
        ChunkIndex = 0
    };
}
