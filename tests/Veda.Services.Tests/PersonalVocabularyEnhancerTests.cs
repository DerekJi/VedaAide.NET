using FluentAssertions;
using NUnit.Framework;
using Veda.Core.Options;
using System.Text.Json;

namespace Veda.Services.Tests;

[TestFixture]
public class PersonalVocabularyEnhancerTests
{
    private PersonalVocabularyEnhancer _enhancer = null!;
    private string _tempVocabFile = null!;

    [SetUp]
    public void SetUp()
    {
        _tempVocabFile = Path.Combine(Path.GetTempPath(), $"vocab-{Guid.NewGuid()}.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_tempVocabFile))
            File.Delete(_tempVocabFile);
    }

    private void CreateVocabularyFile(string jsonContent)
    {
        File.WriteAllText(_tempVocabFile, jsonContent);
        var options = new SemanticsOptions { VocabularyFilePath = _tempVocabFile };
        _enhancer = new PersonalVocabularyEnhancer(options);
    }

    [Test]
    public async Task GetEnhancedMetadataAsync_WithVocabularyMatch_InPlaceReplacesTermWithSynonyms()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = new[]
            {
                new { term = "BG", synonyms = new[] { "背景资料", "context" } }
            },
            tags = Array.Empty<object>()
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        var content = "The BG is too dark, so James had";

        // Act
        var result = await _enhancer.GetEnhancedMetadataAsync(content);

        // Assert
        Assert.That(result.EnrichedContent, Does.Contain("BG (背景资料 context)"));
        Assert.That(result.EnrichedContent, Does.StartWith("The BG (背景资料 context) is too dark"));
        Assert.That(result.DetectedTermsWithSynonyms, Does.ContainKey("BG"));
        Assert.That(result.DetectedTermsWithSynonyms["BG"], Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GetEnhancedMetadataAsync_MultipleSynonyms_IncludesAllInParentheses()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = new[]
            {
                new { term = "Q4", synonyms = new[] { "fourth quarter", "第四季度", "四季度" } }
            },
            tags = Array.Empty<object>()
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        var content = "Q4 revenue was strong";

        // Act
        var result = await _enhancer.GetEnhancedMetadataAsync(content);

        // Assert
        Assert.That(result.EnrichedContent, Does.Contain("Q4 (fourth quarter 第四季度 四季度)"));
        Assert.That(result.DetectedTermsWithSynonyms["Q4"], Has.Count.EqualTo(3));
    }

    [Test]
    public async Task GetEnhancedMetadataAsync_CaseInsensitiveMatch_ReplacesWithOriginalCase()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = new[]
            {
                new { term = "bg", synonyms = new[] { "background" } }
            },
            tags = Array.Empty<object>()
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        var content = "The BG is dark, but the background was light"; // BG (uppercase variation)

        // Act
        var result = await _enhancer.GetEnhancedMetadataAsync(content);

        // Assert
        // Replacement preserves the original case of matched term
        Assert.That(result.EnrichedContent, Does.Contain("BG (background)"));
        Assert.That(result.DetectedTermsWithSynonyms["bg"], Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetEnhancedMetadataAsync_NoVocabularyMatch_NoReplacement()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = new[]
            {
                new { term = "BG", synonyms = new[] { "background" } }
            },
            tags = Array.Empty<object>()
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        var content = "The scene is too dark";

        // Act
        var result = await _enhancer.GetEnhancedMetadataAsync(content);

        // Assert
        Assert.That(result.EnrichedContent, Is.EqualTo(content));
        Assert.That(result.DetectedTermsWithSynonyms, Is.Empty);
    }

    [Test]
    public async Task GetEnhancedMetadataAsync_WithTagsAndVocabulary_BothDetected()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = new[]
            {
                new { term = "invoice", synonyms = new[] { "bill", "receipt" } }
            },
            tags = new[]
            {
                new { pattern = "invoice|payment", labels = new[] { "finance", "billing" } }
            }
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        var content = "Please send the invoice for payment";

        // Act
        var result = await _enhancer.GetEnhancedMetadataAsync(content);

        // Assert
        Assert.That(result.AliasTags, Does.Contain("finance"));
        Assert.That(result.AliasTags, Does.Contain("billing"));
        Assert.That(result.EnrichedContent, Does.Contain("invoice (bill receipt)"));
    }

    [Test]
    public async Task ExpandQueryAsync_AppliesSynonymExpansion_ToQuery()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = new[]
            {
                new { term = "BG", synonyms = new[] { "background", "context" } }
            },
            tags = Array.Empty<object>()
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        var query = "What about BG?";

        // Act
        var expanded = await _enhancer.ExpandQueryAsync(query);

        // Assert
        Assert.That(expanded, Does.Contain("BG"));
        Assert.That(expanded, Does.Contain("background"));
        Assert.That(expanded, Does.Contain("context"));
    }

    [Test]
    public async Task GetAliasTagsAsync_BackwardCompatibility_ReturnsTags()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = Array.Empty<object>(),
            tags = new[]
            {
                new { pattern = "health|checkup", labels = new[] { "health", "medical" } }
            }
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        var content = "I had a health checkup today";

        // Act
        var tags = await _enhancer.GetAliasTagsAsync(content);

        // Assert
        Assert.That(tags, Does.Contain("health"));
        Assert.That(tags, Does.Contain("medical"));
    }

    [Test]
    public async Task GetEnhancedMetadataAsync_AlreadyReplacedTerm_NotDoubleReplaced()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = new[]
            {
                new { term = "test", synonyms = new[] { "exam" } }
            },
            tags = Array.Empty<object>()
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        // Content already has parentheses with test
        var content = "test (exam) is important";

        // Act
        var result = await _enhancer.GetEnhancedMetadataAsync(content);

        // Assert
        // Should not double-replace: should remain "test (exam)"
        Assert.That(result.EnrichedContent, Is.EqualTo("test (exam) is important"));
    }

    [Test]
    public async Task GetEnhancedMetadataAsync_EmptyVocabulary_ReturnsOriginalContent()
    {
        // Arrange
        var vocab = new
        {
            vocabulary = Array.Empty<object>(),
            tags = Array.Empty<object>()
        };
        CreateVocabularyFile(JsonSerializer.Serialize(vocab));

        var content = "Some plain text";

        // Act
        var result = await _enhancer.GetEnhancedMetadataAsync(content);

        // Assert
        Assert.That(result.EnrichedContent, Is.EqualTo(content));
        Assert.That(result.AliasTags, Is.Empty);
        Assert.That(result.DetectedTermsWithSynonyms, Is.Empty);
    }
}
