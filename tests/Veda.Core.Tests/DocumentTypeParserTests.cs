using FluentAssertions;
using NUnit.Framework;
using Veda.Core;

namespace Veda.Core.Tests;

[TestFixture]
public class DocumentTypeParserTests
{
    [TestCase("BillInvoice",   DocumentType.BillInvoice)]
    [TestCase("billinvoice",   DocumentType.BillInvoice)]
    [TestCase("BILLINVOICE",   DocumentType.BillInvoice)]
    [TestCase("Specification", DocumentType.Specification)]
    [TestCase("Report",        DocumentType.Report)]
    [TestCase("Other",         DocumentType.Other)]
    public void ParseOrDefault_ValidString_ShouldReturnCorrectType(string input, DocumentType expected)
    {
        var result = DocumentTypeParser.ParseOrDefault(input);
        result.Should().Be(expected);
    }

    [TestCase("unknown")]
    [TestCase("invalid")]
    [TestCase("")]
    [TestCase("  ")]
    public void ParseOrDefault_InvalidOrEmptyString_ShouldReturnDefaultType(string input)
    {
        var result = DocumentTypeParser.ParseOrDefault(input);
        result.Should().Be(DocumentType.Other);
    }

    [Test]
    public void ParseOrDefault_NullString_ShouldReturnDefaultType()
    {
        var result = DocumentTypeParser.ParseOrDefault(null);
        result.Should().Be(DocumentType.Other);
    }

    [Test]
    public void ParseOrDefault_InvalidString_WithExplicitDefault_ShouldReturnExplicitDefault()
    {
        var result = DocumentTypeParser.ParseOrDefault("invalid", DocumentType.Report);
        result.Should().Be(DocumentType.Report);
    }

    [TestCase("BillInvoice",   DocumentType.BillInvoice)]
    [TestCase("Specification", DocumentType.Specification)]
    [TestCase("report",        DocumentType.Report)]
    public void ParseOrNull_ValidString_ShouldReturnCorrectNullableType(string input, DocumentType expected)
    {
        var result = DocumentTypeParser.ParseOrNull(input);
        result.Should().Be(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("gibberish")]
    public void ParseOrNull_InvalidOrEmptyString_ShouldReturnNull(string? input)
    {
        var result = DocumentTypeParser.ParseOrNull(input);
        result.Should().BeNull();
    }
}
