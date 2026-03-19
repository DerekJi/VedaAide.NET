using FluentAssertions;
using NUnit.Framework;

namespace Veda.Core.Tests;

[TestFixture]
public class ChunkingOptionsTests
{
    [Test]
    public void ForDocumentType_BillInvoice_ShouldReturn256TokenSize()
    {
        var opts = ChunkingOptions.ForDocumentType(DocumentType.BillInvoice);
        opts.TokenSize.Should().Be(256);
        opts.OverlapTokens.Should().Be(32);
    }

    [Test]
    public void ForDocumentType_Specification_ShouldReturn1024TokenSize()
    {
        var opts = ChunkingOptions.ForDocumentType(DocumentType.Specification);
        opts.TokenSize.Should().Be(1024);
        opts.OverlapTokens.Should().Be(128);
    }

    [TestCase(DocumentType.Report)]
    [TestCase(DocumentType.Other)]
    public void ForDocumentType_ReportAndOther_ShouldReturn512TokenSize(DocumentType type)
    {
        var opts = ChunkingOptions.ForDocumentType(type);
        opts.TokenSize.Should().Be(512);
        opts.OverlapTokens.Should().Be(64);
    }

    [Test]
    public void ForDocumentType_AllTypes_ShouldHaveOverlapLessThanTokenSize()
    {
        foreach (DocumentType type in Enum.GetValues<DocumentType>())
        {
            var opts = ChunkingOptions.ForDocumentType(type);
            opts.OverlapTokens.Should().BeLessThan(opts.TokenSize,
                because: $"{type}: OverlapTokens should be less than TokenSize");
        }
    }
}
