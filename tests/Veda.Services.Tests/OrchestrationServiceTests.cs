using FluentAssertions;
using Moq;
using NUnit.Framework;
using Veda.Agents.Orchestration;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Services.Tests;

[TestFixture]
public class OrchestrationServiceTests
{
    private Mock<IQueryService>            _queryService        = null!;
    private Mock<IDocumentIngestor>        _documentIngestor    = null!;
    private Mock<IHallucinationGuardService> _hallucinationGuard = null!;
    private OrchestrationService           _sut                 = null!;

    [SetUp]
    public void SetUp()
    {
        _queryService     = new Mock<IQueryService>();
        _documentIngestor = new Mock<IDocumentIngestor>();
        _hallucinationGuard = new Mock<IHallucinationGuardService>();

        _sut = new OrchestrationService(
            _queryService.Object,
            _documentIngestor.Object,
            _hallucinationGuard.Object);
    }

    // ── RunQueryFlowAsync ────────────────────────────────────────────────────

    [Test]
    public async Task RunQueryFlowAsync_ValidQuestion_ShouldCallQueryService()
    {
        _queryService
            .Setup(q => q.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse { Answer = "42", IsHallucination = false });

        var result = await _sut.RunQueryFlowAsync("What is the answer?");

        _queryService.Verify(q => q.QueryAsync(
            It.Is<RagQueryRequest>(r => r.Question == "What is the answer?"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Answer.Should().Be("42");
    }

    [Test]
    public async Task RunQueryFlowAsync_WhenNotHallucination_WithSources_ShouldCallEvalAgent()
    {
        var source = new SourceReference { ChunkContent = "some context", DocumentName = "doc.txt" };
        _queryService
            .Setup(q => q.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse
            {
                Answer           = "answer",
                IsHallucination  = false,
                Sources          = [source]
            });

        _hallucinationGuard
            .Setup(h => h.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.RunQueryFlowAsync("question");

        _hallucinationGuard.Verify(h => h.VerifyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        result.IsEvaluated.Should().BeTrue();
        result.EvaluationSummary.Should().Contain("grounded");
    }

    [Test]
    public async Task RunQueryFlowAsync_WhenHallucination_ShouldSkipEvalAgent()
    {
        _queryService
            .Setup(q => q.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse { Answer = "hallucinated", IsHallucination = true });

        var result = await _sut.RunQueryFlowAsync("question");

        _hallucinationGuard.Verify(h => h.VerifyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        result.IsEvaluated.Should().BeFalse();
        result.AgentTrace.Should().Contain(t => t.Contains("skipped"));
    }

    [Test]
    public async Task RunQueryFlowAsync_ShouldPopulateAgentTrace()
    {
        _queryService
            .Setup(q => q.QueryAsync(It.IsAny<RagQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagQueryResponse { Answer = "ok", IsHallucination = false });

        var result = await _sut.RunQueryFlowAsync("question");

        result.AgentTrace.Should().NotBeEmpty();
        result.AgentTrace.Should().Contain(t => t.Contains("QueryAgent"));
    }

    // ── RunIngestFlowAsync ────────────────────────────────────────────────────

    [Test]
    public async Task RunIngestFlowAsync_ValidContent_ShouldCallDocumentIngestor()
    {
        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestResult("doc-1", "file.txt", 5));

        var result = await _sut.RunIngestFlowAsync("content", "file.txt");

        _documentIngestor.Verify(d => d.IngestAsync(
            "content", "file.txt", It.IsAny<DocumentType>(), It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()), Times.Once);
        result.Answer.Should().Contain("5");
        result.Answer.Should().Contain("file.txt");
    }

    [Test]
    public async Task RunIngestFlowAsync_InvoiceFileName_ShouldInferBillInvoiceType()
    {
        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestResult("d", "invoice_2025.txt", 1));

        await _sut.RunIngestFlowAsync("content", "invoice_2025.txt");

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            DocumentType.BillInvoice,
            It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunIngestFlowAsync_SpecFileName_ShouldInferSpecificationType()
    {
        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestResult("d", "spec.md", 3));

        await _sut.RunIngestFlowAsync("content", "spec.md");

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            DocumentType.Specification,
            It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task RunIngestFlowAsync_UnknownFileName_ShouldInferOtherType()
    {
        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestResult("d", "notes.txt", 2));

        await _sut.RunIngestFlowAsync("content", "notes.txt");

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            DocumentType.Other,
            It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
