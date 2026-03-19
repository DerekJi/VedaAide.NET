using FluentAssertions;
using NUnit.Framework;
using Veda.Core;
using Veda.Services;

namespace Veda.Services.Tests;

[TestFixture]
public class TextDocumentProcessorTests
{
    private readonly TextDocumentProcessor _sut = new();
    private const string DocId = "doc-001";
    private const string DocName = "test.txt";

    [Test]
    public void Process_EmptyContent_ShouldThrowArgumentException()
    {
        var act = () => _sut.Process("   ", DocName, DocumentType.Other, DocId);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Process_EmptyDocumentId_ShouldThrowArgumentException()
    {
        var act = () => _sut.Process("some content", DocName, DocumentType.Other, "");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Process_ShortContent_ShouldReturnSingleChunk()
    {
        var content = "This is a short document with just a few words.";
        var chunks = _sut.Process(content, DocName, DocumentType.Other, DocId);
        chunks.Should().HaveCount(1);
    }

    [Test]
    public void Process_ValidInput_ShouldPreserveDocumentId()
    {
        var content = "Some test content for the document.";
        var chunks = _sut.Process(content, DocName, DocumentType.Other, DocId);
        chunks.Should().AllSatisfy(c => c.DocumentId.Should().Be(DocId));
    }

    [Test]
    public void Process_LongContent_ShouldSplitIntoMultipleChunks()
    {
        // BillInvoice: 256 token budget ≈ 196 words per chunk; 500 words must produce > 1 chunk
        var content = string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}"));
        var chunks = _sut.Process(content, DocName, DocumentType.BillInvoice, DocId);
        chunks.Should().HaveCountGreaterThan(1);
    }

    [Test]
    public void Process_MultipleChunks_ShouldHaveSequentialChunkIndex()
    {
        var content = string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}"));
        var chunks = _sut.Process(content, DocName, DocumentType.BillInvoice, DocId);
        for (var i = 0; i < chunks.Count; i++)
            chunks[i].ChunkIndex.Should().Be(i);
    }

    [Test]
    public void Process_ValidInput_ShouldSetDocumentName()
    {
        var content = "Test content for document name check.";
        var chunks = _sut.Process(content, DocName, DocumentType.Other, DocId);
        chunks.Should().AllSatisfy(c => c.DocumentName.Should().Be(DocName));
    }

    [Test]
    public void Process_ValidInput_ShouldSetDocumentType()
    {
        var content = "Test content for type check.";
        var chunks = _sut.Process(content, DocName, DocumentType.Specification, DocId);
        chunks.Should().AllSatisfy(c => c.DocumentType.Should().Be(DocumentType.Specification));
    }

    [Test]
    public void Process_MultipleChunks_ShouldAssignUniqueIds()
    {
        var content = string.Join(" ", Enumerable.Range(1, 500).Select(i => $"word{i}"));
        var chunks = _sut.Process(content, DocName, DocumentType.BillInvoice, DocId);
        chunks.Select(c => c.Id).Should().OnlyHaveUniqueItems();
    }
}
