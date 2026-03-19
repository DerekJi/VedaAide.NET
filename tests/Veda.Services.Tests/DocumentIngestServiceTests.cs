using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Services;

namespace Veda.Services.Tests;

[TestFixture]
public class DocumentIngestServiceTests
{
    private Mock<IDocumentProcessor> _processor = null!;
    private Mock<IEmbeddingService> _embedding = null!;
    private Mock<IVectorStore> _vectorStore = null!;
    private Mock<ILogger<DocumentIngestService>> _logger = null!;
    private DocumentIngestService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _processor = new Mock<IDocumentProcessor>();
        _embedding = new Mock<IEmbeddingService>();
        _vectorStore = new Mock<IVectorStore>();
        _logger = new Mock<ILogger<DocumentIngestService>>();
        _sut = new DocumentIngestService(
            _processor.Object,
            _embedding.Object,
            _vectorStore.Object,
            _logger.Object);
    }

    [Test]
    public async Task IngestAsync_EmptyContent_ShouldThrowArgumentException()
    {
        var act = () => _sut.IngestAsync("  ", "doc.txt", DocumentType.Other);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task IngestAsync_EmptyDocumentName_ShouldThrowArgumentException()
    {
        var act = () => _sut.IngestAsync("some content", "", DocumentType.Other);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task IngestAsync_ValidInput_ShouldCallProcessorWithGeneratedDocumentId()
    {
        // Arrange
        var capturedDocumentId = string.Empty;
        _processor
            .Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
            .Callback<string, string, DocumentType, string>((_, _, _, id) => capturedDocumentId = id)
            .Returns([MakeChunk("chunk1")]);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[3] });
        _vectorStore
            .Setup(v => v.UpsertBatchAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.IngestAsync("content", "doc.txt", DocumentType.Other);

        // Assert
        capturedDocumentId.Should().NotBeEmpty();
        result.DocumentId.Should().Be(capturedDocumentId);
    }

    [Test]
    public async Task IngestAsync_ValidInput_ShouldReturnCorrectChunksStored()
    {
        // Arrange
        var chunks = new[] { MakeChunk("a"), MakeChunk("b"), MakeChunk("c") };
        _processor
            .Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
            .Returns(chunks);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[3], new float[3], new float[3] });
        _vectorStore
            .Setup(v => v.UpsertBatchAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.IngestAsync("content", "doc.txt", DocumentType.Other);

        // Assert
        result.ChunksStored.Should().Be(3);
        result.DocumentName.Should().Be("doc.txt");
    }

    [Test]
    public async Task IngestAsync_ValidInput_ShouldAssignEmbeddingsToChunks()
    {
        // Arrange
        var chunk = MakeChunk("hello world");
        var expectedEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        _processor
            .Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
            .Returns([chunk]);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { expectedEmbedding });
        IEnumerable<DocumentChunk>? storedChunks = null;
        _vectorStore
            .Setup(v => v.UpsertBatchAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<DocumentChunk>, CancellationToken>((c, _) => storedChunks = c.ToList())
            .Returns(Task.CompletedTask);

        // Act
        await _sut.IngestAsync("hello world", "doc.txt", DocumentType.Other);

        // Assert
        storedChunks.Should().NotBeNull();
        storedChunks!.First().Embedding.Should().BeEquivalentTo(expectedEmbedding);
    }

    [Test]
    public async Task IngestAsync_ValidInput_ShouldCallUpsertBatchExactlyOnce()
    {
        // Arrange
        _processor
            .Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
            .Returns([MakeChunk("chunk1"), MakeChunk("chunk2")]);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[3], new float[3] });
        _vectorStore
            .Setup(v => v.UpsertBatchAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.IngestAsync("content", "doc.txt", DocumentType.Other);

        // Assert
        _vectorStore.Verify(v => v.UpsertBatchAsync(
            It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DocumentChunk MakeChunk(string content) => new()
    {
        Id = Guid.NewGuid().ToString(),
        DocumentId = "doc-id",
        DocumentName = "test.txt",
        DocumentType = DocumentType.Other,
        Content = content,
        ChunkIndex = 0
    };
}
