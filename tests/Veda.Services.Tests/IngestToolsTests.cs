using FluentAssertions;
using Moq;
using NUnit.Framework;
using System.Text.Json;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.MCP.Tools;

namespace Veda.Services.Tests;

[TestFixture]
public class IngestToolsTests
{
    private Mock<IDocumentIngestor> _documentIngestor = null!;
    private IngestTools             _sut              = null!;

    [SetUp]
    public void SetUp()
    {
        _documentIngestor = new Mock<IDocumentIngestor>();
        _sut = new IngestTools(_documentIngestor.Object);
    }

    [Test]
    public async Task IngestDocument_ValidInput_ShouldReturnJsonWithDocumentId()
    {
        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestResult("doc-123", "test.txt", 7));

        var json = await _sut.IngestDocument("some content", "test.txt");

        var item = JsonSerializer.Deserialize<JsonElement>(json);
        item.GetProperty("documentId").GetString().Should().Be("doc-123");
        item.GetProperty("chunksStored").GetInt32().Should().Be(7);
        item.GetProperty("documentName").GetString().Should().Be("test.txt");
    }

    [Test]
    public void IngestDocument_EmptyContent_ShouldThrowArgumentException()
    {
        var act = async () => await _sut.IngestDocument("  ", "test.txt");
        act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public void IngestDocument_EmptyDocumentName_ShouldThrowArgumentException()
    {
        var act = async () => await _sut.IngestDocument("content", "");
        act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task IngestDocument_ValidDocumentType_ShouldPassParsedTypeToIngestor()
    {
        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestResult("d", "spec.txt", 1));

        await _sut.IngestDocument("content", "spec.txt", "Specification");

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            DocumentType.Specification,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task IngestDocument_InvalidDocumentType_ShouldFallbackToOther()
    {
        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestResult("d", "file.txt", 1));

        await _sut.IngestDocument("content", "file.txt", "SomethingUnknown");

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            DocumentType.Other,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task IngestDocument_DefaultDocumentType_ShouldUseOther()
    {
        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestResult("d", "notes.txt", 2));

        await _sut.IngestDocument("content", "notes.txt"); // documentType defaults to "Other"

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            DocumentType.Other,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
