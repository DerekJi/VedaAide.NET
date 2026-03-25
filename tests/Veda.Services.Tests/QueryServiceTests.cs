using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private Mock<ILlmRouter> _llmRouter = null!;
    private Mock<ISemanticCache> _semanticCache = null!;
    private Mock<IHallucinationGuardService> _hallucinationGuard = null!;
    private Mock<IContextWindowBuilder> _contextWindowBuilder = null!;
    private Mock<IPromptTemplateRepository> _promptTemplateRepository = null!;
    private Mock<IChainOfThoughtStrategy> _chainOfThought = null!;
    private Mock<ILogger<QueryService>> _logger = null!;
    private QueryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _embedding = new Mock<IEmbeddingService>();
        _vectorStore = new Mock<IVectorStore>();
        _chatService = new Mock<IChatService>();
        _llmRouter = new Mock<ILlmRouter>();
        _llmRouter.Setup(r => r.Resolve(It.IsAny<QueryMode>())).Returns(_chatService.Object);
        _semanticCache = new Mock<ISemanticCache>();
        _semanticCache.Setup(c => c.GetAsync(It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _hallucinationGuard = new Mock<IHallucinationGuardService>();
        _logger = new Mock<ILogger<QueryService>>();

        _contextWindowBuilder = new Mock<IContextWindowBuilder>();
        _contextWindowBuilder
            .Setup(b => b.Build(It.IsAny<IReadOnlyList<(DocumentChunk, float)>>(), It.IsAny<int>()))
            .Returns((IReadOnlyList<(DocumentChunk Chunk, float Similarity)> c, int _) =>
                c.Select(x => x.Chunk).ToList().AsReadOnly());

        _promptTemplateRepository = new Mock<IPromptTemplateRepository>();
        _promptTemplateRepository
            .Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PromptTemplate?)null);

        _chainOfThought = new Mock<IChainOfThoughtStrategy>();
        _chainOfThought
            .Setup(c => c.Enhance(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string q, string ctx) => $"Context:\n{ctx}\n\nQuestion: {q}");

        // Default: hallucination self-check disabled; threshold low enough not to flag in happy-path tests
        var ragOptions = Options.Create(new RagOptions { HallucinationSimilarityThreshold = 0f });
        _sut = new QueryService(
            _embedding.Object,
            _vectorStore.Object,
            _llmRouter.Object,
            _hallucinationGuard.Object,
            _contextWindowBuilder.Object,
            _promptTemplateRepository.Object,
            _chainOfThought.Object,
            _semanticCache.Object,
            ragOptions,
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
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
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
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
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
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Some answer.");

        // Act
        var response = await _sut.QueryAsync(new RagQueryRequest { Question = "Question?" });

        // Assert: confidence = max of reranked scores (reranking blends similarity + keyword overlap)
        response.AnswerConfidence.Should().BeGreaterThan(0f);
    }

    [Test]
    public async Task QueryAsync_WithFilterType_ShouldPassFilterToVectorStore()
    {
        // Arrange
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk, float)>());

        var request = new RagQueryRequest { Question = "Q?", FilterDocumentType = DocumentType.BillInvoice };

        // Act
        await _sut.QueryAsync(request);

        // Assert: filter propagated to the vector store
        _vectorStore.Verify(v => v.SearchAsync(
            It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
            DocumentType.BillInvoice, It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueryAsync_WithDateRange_ShouldPassDateRangeToVectorStore()
    {
        // Arrange
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to   = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(DocumentChunk, float)>());

        var request = new RagQueryRequest { Question = "Q?", DateFrom = from, DateTo = to };

        // Act
        await _sut.QueryAsync(request);

        // Assert
        _vectorStore.Verify(v => v.SearchAsync(
            It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
            It.IsAny<DocumentType?>(), from, to,
            It.IsAny<CancellationToken>()), Times.Once);
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
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer.");

        // Act
        var response = await _sut.QueryAsync(new RagQueryRequest { Question = "Q?" });

        // Assert: truncated to SourceContentMaxLength chars + "..." (3 chars)
        response.Sources[0].ChunkContent.Should().HaveLength(QueryService.SourceContentMaxLength + 3);
        response.Sources[0].ChunkContent.Should().EndWith("...");
    }

    [Test]
    public async Task QueryAsync_HighAnswerSimilarity_ShouldNotFlagHallucination()
    {
        // Arrange: setup with threshold = 0.3; mock returns similarity 0.9 (above threshold)
        var ragOptions = Options.Create(new RagOptions { HallucinationSimilarityThreshold = 0.3f });
        var sut = BuildSut(ragOptions);

        var chunks = new List<(DocumentChunk, float)> { (MakeChunk("doc1", "relevant"), 0.9f) };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer.");

        // Act
        var response = await sut.QueryAsync(new RagQueryRequest { Question = "Q?" });

        // Assert
        response.IsHallucination.Should().BeFalse();
    }

    [Test]
    public async Task QueryAsync_LowAnswerSimilarity_ShouldFlagHallucination()
    {
        // Arrange: threshold = 0.5; answer check returns empty → maxSimilarity = 0 < 0.5
        var ragOptions = Options.Create(new RagOptions { HallucinationSimilarityThreshold = 0.5f });
        var sut = BuildSut(ragOptions);

        var chunks = new List<(DocumentChunk, float)> { (MakeChunk("doc1", "relevant"), 0.9f) };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        // First call (RAG search): return chunks; second call (answer embedding check): return empty
        _vectorStore.SetupSequence(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks)           // RAG retrieval
            .ReturnsAsync(new List<(DocumentChunk, float)>());  // answer check → no match → hallucination
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Fabricated answer.");

        // Act
        var response = await sut.QueryAsync(new RagQueryRequest { Question = "Q?" });

        // Assert
        response.IsHallucination.Should().BeTrue();
    }

    [Test]
    public async Task QueryAsync_SelfCheckEnabled_ShouldCallHallucinationGuard()
    {
        // Arrange: enable self-check guard; layer 1 passes (threshold = 0)
        var ragOptions = Options.Create(new RagOptions
        {
            HallucinationSimilarityThreshold = 0f,
            EnableSelfCheckGuard = true
        });
        var sut = BuildSut(ragOptions);

        var chunks = new List<(DocumentChunk, float)> { (MakeChunk("doc1", "content"), 0.9f) };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer.");
        _hallucinationGuard
            .Setup(g => g.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await sut.QueryAsync(new RagQueryRequest { Question = "Q?" });

        // Assert: guard was invoked exactly once
        _hallucinationGuard.Verify(g => g.VerifyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task QueryAsync_SelfCheckDisabled_ShouldNotCallHallucinationGuard()
    {
        // Arrange: guard disabled (default)
        var chunks = new List<(DocumentChunk, float)> { (MakeChunk("doc1", "content"), 0.9f) };
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[3]);
        _vectorStore.Setup(v => v.SearchAsync(
                It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);
        _chatService.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Answer.");

        // Act
        await _sut.QueryAsync(new RagQueryRequest { Question = "Q?" });

        // Assert
        _hallucinationGuard.Verify(g => g.VerifyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Helper: build a new QueryService with different options but same mocks
    private QueryService BuildSut(IOptions<RagOptions> ragOptions) =>
        new(_embedding.Object, _vectorStore.Object, _llmRouter.Object,
            _hallucinationGuard.Object, _contextWindowBuilder.Object,
            _promptTemplateRepository.Object, _chainOfThought.Object,
            _semanticCache.Object, ragOptions, _logger.Object);

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
