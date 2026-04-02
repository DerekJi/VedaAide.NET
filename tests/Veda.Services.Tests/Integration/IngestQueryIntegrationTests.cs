using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Services;
using Veda.Storage;

namespace Veda.Services.Tests.Integration;

/// <summary>
/// Full-pipeline integration tests: ingest text into in-memory SQLite → vector search → answer generation.
///
/// Zero external dependencies:
///   - No Ollama / Azure OpenAI  → replaced by FakeEmbeddingService and FakeChatService
///   - No disk writes            → SQLite DataSource=:memory: is destroyed after each test
///
/// What is tested here (real implementations, not mocked):
///   - TextDocumentProcessor   (chunking)
///   - SqliteVectorStore       (upsert, search, versioning, dedup)
///   - DocumentIngestService   (full ingest pipeline)
///   - QueryService            (embedding → vector search → context window → LLM)
/// </summary>
[TestFixture]
[Category("Integration")]
public class IngestQueryIntegrationTests
{
    private SqliteConnection      _connection  = null!;
    private VedaDbContext         _db          = null!;
    private SqliteVectorStore     _vectorStore = null!;
    private FakeEmbeddingService  _embedding   = null!;
    private FakeChatService       _chat        = null!;
    private DocumentIngestService _ingestor    = null!;
    private QueryService          _query       = null!;

    [SetUp]
    public async Task SetUp()
    {
        // ── In-memory SQLite — scoped to this test only ────────────────────
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _db = new VedaDbContext(
            new DbContextOptionsBuilder<VedaDbContext>()
                .UseSqlite(_connection)
                .Options);
        await _db.Database.EnsureCreatedAsync();

        _vectorStore = new SqliteVectorStore(_db);
        _embedding   = new FakeEmbeddingService();
        _chat        = new FakeChatService();

        var vedaOpts = Options.Create(new VedaOptions { EmbeddingModel = "fake" });
        var ragOpts  = Options.Create(new RagOptions
        {
            HybridRetrievalEnabled = false,
            EnableSelfCheckGuard   = false
        });

        // ── Stub file extractors (only called by IngestFileAsync, not IngestAsync) ──
        var docIntelExtractor = new DocumentIntelligenceFileExtractor(
            Options.Create(new DocumentIntelligenceOptions()),
            NullLogger<DocumentIntelligenceFileExtractor>.Instance,
            new AzureDiQuotaState());

        var visionExtractor = new VisionModelFileExtractor(
            new Mock<IChatCompletionService>().Object,
            Options.Create(new VisionOptions { Enabled = false }),
            NullLogger<VisionModelFileExtractor>.Instance);

        var pdfTextLayerExtractor = new PdfTextLayerExtractor(
            NullLogger<PdfTextLayerExtractor>.Instance);

        // ── Mocks for cross-cutting concerns ──────────────────────────────
        var mockSemanticCache = new Mock<ISemanticCache>();
        mockSemanticCache
            .Setup(c => c.GetAsync(It.IsAny<float[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        mockSemanticCache
            .Setup(c => c.SetAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockSemanticEnhancer = new Mock<ISemanticEnhancer>();
        mockSemanticEnhancer
            .Setup(e => e.ExpandQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, CancellationToken _) => q);
        mockSemanticEnhancer
            .Setup(e => e.GetAliasTagsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var mockDocumentDiff = new Mock<IDocumentDiffService>();
        mockDocumentDiff
            .Setup(d => d.DiffAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentChangeSummary("id", 0, 0, 0, [], DateTimeOffset.UtcNow));

        // ── DocumentIngestService (real processor + fake embedding + in-memory DB) ──
        _ingestor = new DocumentIngestService(
            new TextDocumentProcessor(),
            _embedding,
            _vectorStore,
            mockSemanticCache.Object,
            mockSemanticEnhancer.Object,
            mockDocumentDiff.Object,
            vedaOpts,
            docIntelExtractor,
            visionExtractor,
            pdfTextLayerExtractor,
            NullLogger<DocumentIngestService>.Instance);

        // ── QueryService stubs ────────────────────────────────────────────
        var mockLlmRouter = new Mock<ILlmRouter>();
        mockLlmRouter.Setup(r => r.Resolve(It.IsAny<QueryMode>())).Returns(_chat);

        var mockHallucinationGuard = new Mock<IHallucinationGuardService>();
        mockHallucinationGuard
            .Setup(h => h.VerifyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // false = no hallucination detected

        var mockContextWindow = new Mock<IContextWindowBuilder>();
        mockContextWindow
            .Setup(b => b.Build(
                It.IsAny<IReadOnlyList<(DocumentChunk Chunk, float Similarity)>>(),
                It.IsAny<int>()))
            .Returns((IReadOnlyList<(DocumentChunk Chunk, float)> candidates, int _) =>
                (IReadOnlyList<DocumentChunk>)candidates.Select(c => c.Chunk).ToList());

        var mockPromptRepo = new Mock<IPromptTemplateRepository>();
        mockPromptRepo
            .Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PromptTemplate?)null);

        var mockChainOfThought = new Mock<IChainOfThoughtStrategy>();
        mockChainOfThought
            .Setup(c => c.Enhance(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string q, string ctx) => $"{q}\n\n{ctx}");

        var mockFeedbackBoost = new Mock<IFeedbackBoostService>();
        mockFeedbackBoost
            .Setup(f => f.ApplyBoostAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<(DocumentChunk, float)>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IReadOnlyList<(DocumentChunk, float)> r, CancellationToken _) => r);

        var mockHybridRetriever = new Mock<IHybridRetriever>();

        // ── QueryService (real vector search + fake LLM) ──────────────────
        _query = new QueryService(
            _embedding,
            _vectorStore,
            mockLlmRouter.Object,
            mockHallucinationGuard.Object,
            mockContextWindow.Object,
            mockPromptRepo.Object,
            mockChainOfThought.Object,
            mockSemanticCache.Object,
            mockHybridRetriever.Object,
            mockSemanticEnhancer.Object,
            mockFeedbackBoost.Object,
            ragOpts,
            NullLogger<QueryService>.Instance);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Ingest_TextContent_StoresChunksInMemoryDb()
    {
        var result = await _ingestor.IngestAsync(
            "ICAS Year 5 Mathematics HIGH DISTINCTION MARCO JI ST ANDREW'S SCHOOL 2025",
            "ICAS-Maths",
            DocumentType.Certificate);

        result.ChunksStored.Should().BeGreaterThan(0,
            because: "the text should be split into at least one chunk");
        (await _db.VectorChunks.CountAsync()).Should().BeGreaterThan(0);
    }

    [Test]
    public async Task IngestThenQuery_ReturnsAnswerContainingStoredKeywords()
    {
        // Arrange: ingest two certificates
        const string mathsContent  = "ICAS Year 5 Mathematics HIGH DISTINCTION MARCO JI ST ANDREW'S SCHOOL 2025";
        const string scienceContent = "ICAS Year 5 Science DISTINCTION MARCO JI ST ANDREW'S SCHOOL 2025";

        await _ingestor.IngestAsync(mathsContent,  "ICAS-Maths",   DocumentType.Certificate);
        await _ingestor.IngestAsync(scienceContent, "ICAS-Science", DocumentType.Certificate);

        // Act: query with the maths content text — identical text → cosine 1.0 → top result
        var response = await _query.QueryAsync(new RagQueryRequest
        {
            Question      = mathsContent,
            MinSimilarity = 0.0f,
            TopK          = 5
        }, CancellationToken.None);

        // FakeChatService echoes context, so the answer contains whatever was retrieved
        response.Answer.Should().Contain("MARCO JI");
        response.Answer.Should().Contain("HIGH DISTINCTION");
        response.Sources.Should().NotBeEmpty(
            because: "at least one chunk should be retrieved for a matching query");
    }

    [Test]
    public async Task Certificate_TwoDistinctCertificates_BothStored()
    {
        // DedupThreshold = 1.0f → semantic dedup disabled for Certificate type
        // → same-template certificates for different subjects must coexist
        const string maths   = "ICAS Year 5 Mathematics HIGH DISTINCTION MARCO JI 2025";
        const string english = "ICAS Year 5 English HIGH DISTINCTION MARCO JI 2025";

        await _ingestor.IngestAsync(maths,   "ICAS-Maths",   DocumentType.Certificate);
        await _ingestor.IngestAsync(english, "ICAS-English", DocumentType.Certificate);

        var count = await _db.VectorChunks.CountAsync();
        count.Should().Be(2,
            because: "Certificate DedupThreshold=1.0 means only exact content-hash duplicates are dropped");
    }

    [Test]
    public async Task IngestSameDocumentTwice_SecondVersionSupersedes()
    {
        // First version
        await _ingestor.IngestAsync(
            "Piano Grade 6 Merit MARCO JI AMEB 2025",
            "Piano-Grade6",
            DocumentType.PersonalNote);

        // Second version — different content, same document name
        await _ingestor.IngestAsync(
            "Piano Grade 6 Distinction MARCO JI AMEB 2025",
            "Piano-Grade6",
            DocumentType.PersonalNote);

        // Only the new version's chunks should be active (SupersededAtTicks == 0)
        var active = await _db.VectorChunks
            .Where(c => c.DocumentName == "Piano-Grade6" && c.SupersededAtTicks == 0)
            .ToListAsync();

        active.Should().NotBeEmpty();
        active.Should().AllSatisfy(c =>
            c.Content.Should().Contain("Distinction",
                because: "second-version content should supersede the first"));
    }

    [Test]
    public async Task Query_NoDocumentsIngested_ReturnsNoSources()
    {
        // Empty knowledge base — no chunks stored
        var response = await _query.QueryAsync(new RagQueryRequest
        {
            Question      = "How is Marco's ICAS result?",
            MinSimilarity = 0.0f,
            TopK          = 5
        }, CancellationToken.None);

        response.Sources.Should().BeEmpty(
            because: "no documents were ingested so no chunks can be retrieved");
    }
}
