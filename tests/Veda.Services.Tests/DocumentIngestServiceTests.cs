using Veda.Core.Options;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
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
    private Mock<ISemanticEnhancer> _semanticEnhancer = null!;
    private Mock<ISemanticCache> _semanticCache = null!;
    private Mock<IDocumentDiffService> _documentDiffService = null!;
    private DocumentIntelligenceFileExtractor _docIntelExtractor = null!;
    private VisionModelFileExtractor _visionExtractor = null!;
    private DocumentIngestService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _processor = new Mock<IDocumentProcessor>();
        _embedding = new Mock<IEmbeddingService>();
        _vectorStore = new Mock<IVectorStore>();
        _logger = new Mock<ILogger<DocumentIngestService>>();
        _semanticCache = new Mock<ISemanticCache>();
        _semanticCache.Setup(c => c.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Stub extractors with disabled options (IngestFileAsync not exercised in these tests)
        _docIntelExtractor = new DocumentIntelligenceFileExtractor(
            Options.Create(new DocumentIntelligenceOptions()),
            new Mock<ILogger<DocumentIntelligenceFileExtractor>>().Object,
            new AzureDiQuotaState());
        _visionExtractor = new VisionModelFileExtractor(
            new Mock<IChatCompletionService>().Object,
            Options.Create(new VisionOptions { Enabled = false }),
            new Mock<ILogger<VisionModelFileExtractor>>().Object);

        // Default: no near-duplicate threshold pressure — SearchAsync returns empty
        var vedaOptions = Options.Create(new VedaOptions { EmbeddingModel = "test-model" });

        _semanticEnhancer = new Mock<ISemanticEnhancer>();
        _semanticEnhancer
            .Setup(e => e.GetAliasTagsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>().ToList());
        _documentDiffService = new Mock<IDocumentDiffService>();
        _documentDiffService
            .Setup(d => d.DiffAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentChangeSummary("doc-id", 0, 0, 0, [], DateTimeOffset.UtcNow));

        // Default: SearchAsync (dedup check) returns empty → nothing is a near-duplicate
        _vectorStore
            .Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk, float)>());
        // Default: no existing document → version 1
        _vectorStore
            .Setup(v => v.GetCurrentChunksByDocumentNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentChunk>());

        _sut = new DocumentIngestService(
            _processor.Object,
            _embedding.Object,
            _vectorStore.Object,
            _semanticCache.Object,
            _semanticEnhancer.Object,
            _documentDiffService.Object,
            vedaOptions,
            _docIntelExtractor,
            _visionExtractor,
            new PdfTextLayerExtractor(new Mock<ILogger<PdfTextLayerExtractor>>().Object),
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

    [Test]
    public async Task IngestAsync_NearDuplicateChunk_ShouldSkipItAndReduceChunksStored()
    {
        // Arrange: 2 chunks; second one's SearchAsync returns a similar chunk (near-duplicate)
        var chunk1 = MakeChunk("unique content");
        var chunk2 = MakeChunk("near duplicate content");
        _processor
            .Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
            .Returns([chunk1, chunk2]);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[3], new float[3] });
        _vectorStore
            .Setup(v => v.UpsertBatchAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // First dedup check: chunk1 → no similar found
        // Second dedup check: chunk2 → similar found (near-duplicate detected)
        var existingChunk = MakeChunk("near duplicate content");
        _vectorStore
            .SetupSequence(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk, float)>())         // chunk1: not a duplicate
            .ReturnsAsync([(existingChunk, 0.97f)]);                   // chunk2: near-duplicate detected

        var semanticCache = new Mock<ISemanticCache>();
        semanticCache.Setup(c => c.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = new DocumentIngestService(_processor.Object, _embedding.Object,
            _vectorStore.Object, semanticCache.Object, _semanticEnhancer.Object, _documentDiffService.Object,
            Options.Create(new VedaOptions { EmbeddingModel = "test-model" }), _docIntelExtractor, _visionExtractor,
            new PdfTextLayerExtractor(new Mock<ILogger<PdfTextLayerExtractor>>().Object), _logger.Object);

        // Act
        var result = await sut.IngestAsync("content", "doc.txt", DocumentType.Other);

        // Assert: only 1 chunk stored (chunk2 skipped as near-duplicate)
        result.ChunksStored.Should().Be(1);
    }

    [Test]
    public async Task IngestAsync_AllChunksNearDuplicate_ShouldNotCallUpsertBatch()
    {
        // Arrange
        var chunk = MakeChunk("existing content");
        _processor
            .Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
            .Returns([chunk]);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[3] });
        // Dedup check returns a similar chunk
        _vectorStore
            .Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(MakeChunk("existing"), 0.98f)]);

        var semanticCache2 = new Mock<ISemanticCache>();
        semanticCache2.Setup(c => c.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = new DocumentIngestService(_processor.Object, _embedding.Object,
            _vectorStore.Object, semanticCache2.Object, _semanticEnhancer.Object, _documentDiffService.Object,
            Options.Create(new VedaOptions { EmbeddingModel = "test-model" }), _docIntelExtractor, _visionExtractor,
            new PdfTextLayerExtractor(new Mock<ILogger<PdfTextLayerExtractor>>().Object), _logger.Object);

        // Act
        var result = await sut.IngestAsync("content", "doc.txt", DocumentType.Other);

        // Assert: UpsertBatch never called since nothing new to store
        _vectorStore.Verify(v => v.UpsertBatchAsync(
            It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()), Times.Never);
        result.ChunksStored.Should().Be(0);
    }

    /// <summary>
    /// 回归测试：重复加载相同内容的文档时，所有 chunks 被语义去重跳过，
    /// 此时不应调用 MarkDocumentSupersededAsync，否则原有 chunks 会被标记为已取代
    /// 但没有新 chunks 写入，导致文档从列表消失（无法查询）。
    /// </summary>
    [Test]
    public async Task IngestAsync_ReIngestSameContent_AllChunksDeduped_ShouldNotSupersedePreviousVersion()
    {
        // Arrange: document already has an active chunk (previous version)
        var existingChunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = "old-doc-id",
            DocumentName = "doc.txt",
            DocumentType = DocumentType.Other,
            Content = "same content",
            ChunkIndex = 0
        };
        _vectorStore
            .Setup(v => v.GetCurrentChunksByDocumentNameAsync("doc.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync([existingChunk]);

        var newChunk = MakeChunk("same content");
        _processor
            .Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
            .Returns([newChunk]);
        _embedding
            .Setup(e => e.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[3] });

        // Semantic dedup: new chunk is near-identical to existing → skipped
        _vectorStore
            .Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(existingChunk, 0.99f)]);

        var semanticCache3 = new Mock<ISemanticCache>();
        semanticCache3.Setup(c => c.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = new DocumentIngestService(_processor.Object, _embedding.Object,
            _vectorStore.Object, semanticCache3.Object, _semanticEnhancer.Object, _documentDiffService.Object,
            Options.Create(new VedaOptions { EmbeddingModel = "test-model" }), _docIntelExtractor, _visionExtractor,
            new PdfTextLayerExtractor(new Mock<ILogger<PdfTextLayerExtractor>>().Object), _logger.Object);

        // Act
        var result = await sut.IngestAsync("same content", "doc.txt", DocumentType.Other);

        // Assert: MarkDocumentSupersededAsync must NOT be called — the old chunks must remain active.
        // Previously this was a bug: supersede was called unconditionally, wiping the document from the list.
        _vectorStore.Verify(v => v.MarkDocumentSupersededAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStore.Verify(v => v.UpsertBatchAsync(
            It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()), Times.Never);
        result.ChunksStored.Should().Be(0);
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
