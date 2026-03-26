using FluentAssertions;
using Moq;
using NUnit.Framework;
using System.Text.Json;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.MCP.Tools;

namespace Veda.Services.Tests;

[TestFixture]
public class KnowledgeBaseToolsTests
{
    private Mock<IEmbeddingService> _embedding   = null!;
    private Mock<IVectorStore>      _vectorStore = null!;
    private KnowledgeBaseTools      _sut         = null!;

    [SetUp]
    public void SetUp()
    {
        _embedding   = new Mock<IEmbeddingService>();
        _vectorStore = new Mock<IVectorStore>();
        _sut = new KnowledgeBaseTools(_embedding.Object, _vectorStore.Object);

        _embedding
            .Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[384]);
    }

    private static DocumentChunk MakeChunk(string name, string content, DocumentType type = DocumentType.Other) =>
        new() { DocumentId = Guid.NewGuid().ToString(), DocumentName = name, Content = content, DocumentType = type, ChunkIndex = 0 };

    // ── SearchKnowledgeBase ───────────────────────────────────────────────────

    [Test]
    public async Task SearchKnowledgeBase_WithResults_ShouldReturnJsonArray()
    {
        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(MakeChunk("doc.txt", "Hello world"), 0.8f)]);

        var json = await _sut.SearchKnowledgeBase("hello");

        var items = JsonSerializer.Deserialize<JsonElement[]>(json)!;
        items.Should().HaveCount(1);
        items[0].GetProperty("documentName").GetString().Should().Be("doc.txt");
        items[0].GetProperty("content").GetString().Should().Be("Hello world");
        items[0].GetProperty("similarity").GetDouble().Should().BeApproximately(0.8, 0.001);
        items[0].GetProperty("documentType").GetString().Should().Be("Other");
    }

    [Test]
    public async Task SearchKnowledgeBase_NoResults_ShouldReturnEmptyJsonArray()
    {
        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var json = await _sut.SearchKnowledgeBase("query");

        var items = JsonSerializer.Deserialize<JsonElement[]>(json)!;
        items.Should().BeEmpty();
    }

    [Test]
    public void SearchKnowledgeBase_EmptyQuery_ShouldThrowArgumentException()
    {
        var act = async () => await _sut.SearchKnowledgeBase("   ");
        act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task SearchKnowledgeBase_TopKClamped_ShouldClampTo20()
    {
        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.SearchKnowledgeBase("q", topK: 999);

        _vectorStore.Verify(v => v.SearchAsync(
            It.IsAny<float[]>(), 20, It.IsAny<float>(),
            It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task SearchKnowledgeBase_ContentOver500Chars_ShouldTruncateWithEllipsis()
    {
        var longContent = new string('x', 600);
        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(MakeChunk("doc.txt", longContent), 0.7f)]);

        var json = await _sut.SearchKnowledgeBase("q");

        var items = JsonSerializer.Deserialize<JsonElement[]>(json)!;
        var content = items[0].GetProperty("content").GetString()!;
        content.Should().EndWith("...");
        content.Length.Should().BeLessThanOrEqualTo(503); // 500 + "..."
    }

    // ── ListDocuments ─────────────────────────────────────────────────────────

    [Test]
    public async Task ListDocuments_MultipleChunks_ShouldGroupByDocument()
    {
        var docId = Guid.NewGuid().ToString();
        var chunk1 = new DocumentChunk { DocumentId = docId, DocumentName = "report.txt", Content = "chunk1", DocumentType = DocumentType.Report, ChunkIndex = 0 };
        var chunk2 = new DocumentChunk { DocumentId = docId, DocumentName = "report.txt", Content = "chunk2", DocumentType = DocumentType.Report, ChunkIndex = 1 };
        var otherChunk = new DocumentChunk { DocumentId = Guid.NewGuid().ToString(), DocumentName = "other.txt", Content = "c", DocumentType = DocumentType.Other, ChunkIndex = 0 };

        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([(chunk1, 0f), (chunk2, 0f), (otherChunk, 0f)]);

        var json = await _sut.ListDocuments();

        var items = JsonSerializer.Deserialize<JsonElement[]>(json)!;
        items.Should().HaveCount(2);

        var report = items.FirstOrDefault(i => i.GetProperty("documentName").GetString() == "report.txt");
        report.GetProperty("chunkCount").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task ListDocuments_EmptyStore_ShouldReturnEmptyJsonArray()
    {
        _vectorStore
            .Setup(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(),
                It.IsAny<DocumentType?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                It.IsAny<KnowledgeScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var json = await _sut.ListDocuments();

        var items = JsonSerializer.Deserialize<JsonElement[]>(json)!;
        items.Should().BeEmpty();
    }
}
