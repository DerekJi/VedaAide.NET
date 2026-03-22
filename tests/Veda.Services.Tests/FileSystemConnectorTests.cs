using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Services.DataSources;

namespace Veda.Services.Tests;

[TestFixture]
public class FileSystemConnectorTests
{
    private Mock<IDocumentIngestor>      _documentIngestor = null!;
    private Mock<ISyncStateStore>        _syncStateStore   = null!;
    private Mock<ILogger<FileSystemConnector>> _logger     = null!;
    private string                       _tempDir          = null!;

    [SetUp]
    public void SetUp()
    {
        _documentIngestor = new Mock<IDocumentIngestor>();
        _syncStateStore   = new Mock<ISyncStateStore>();
        _logger           = new Mock<ILogger<FileSystemConnector>>();
        _tempDir          = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);

        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string name, DocumentType _, CancellationToken _) =>
                new IngestResult(Guid.NewGuid().ToString(), name, 1));

        // Default: no previous sync record — every file is treated as new
        _syncStateStore
            .Setup(s => s.GetContentHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FileSystemConnector Build(bool enabled = true, string? path = null, string[]? extensions = null)
    {
        var opts = new FileSystemConnectorOptions
        {
            Enabled    = enabled,
            Path       = path ?? _tempDir,
            Extensions = extensions ?? [".txt", ".md"]
        };
        return new FileSystemConnector(
            _documentIngestor.Object,
            _syncStateStore.Object,
            Options.Create(opts),
            _logger.Object);
    }

    [Test]
    public async Task SyncAsync_WhenDisabled_ShouldSkipWithZeroCounts()
    {
        var sut    = Build(enabled: false);
        var result = await sut.SyncAsync();

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.FilesProcessed.Should().Be(0);
        result.ChunksStored.Should().Be(0);
    }

    [Test]
    public async Task SyncAsync_WhenPathNotConfigured_ShouldSkipWithZeroCounts()
    {
        var sut    = Build(enabled: true, path: "");
        var result = await sut.SyncAsync();

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.FilesProcessed.Should().Be(0);
    }

    [Test]
    public async Task SyncAsync_PathDoesNotExist_ShouldReturnErrorAndZeroCounts()
    {
        var sut    = Build(path: Path.Combine(Path.GetTempPath(), "this_dir_should_not_exist_xyz"));
        var result = await sut.SyncAsync();

        result.FilesProcessed.Should().Be(0);
        result.Errors.Should().ContainSingle(e => e.Contains("not found"));
    }

    [Test]
    public async Task SyncAsync_WithTwoTxtFiles_ShouldCallIngestTwice()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "content a");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.txt"), "content b");

        var result = await Build().SyncAsync();

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        result.FilesProcessed.Should().Be(2);
        result.ChunksStored.Should().Be(2);
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task SyncAsync_WhenIngestThrows_ShouldContinueAndRecordError()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "good.txt"), "ok");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "bad.txt"), "fail");

        _documentIngestor
            .Setup(d => d.IngestAsync(It.IsAny<string>(), "bad.txt", It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding failed"));

        var result = await Build().SyncAsync();

        result.FilesProcessed.Should().Be(1);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("bad.txt");
    }

    [Test]
    public async Task SyncAsync_NonMatchingExtension_ShouldSkipFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "document.pdf"), "pdf content");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "note.txt"), "txt content");

        var result = await Build(extensions: [".txt"]).SyncAsync();

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()),
            Times.Once);
        result.FilesProcessed.Should().Be(1);
    }

    [Test]
    public async Task SyncAsync_WhenFileUnchanged_ShouldSkipIngest()
    {
        var filePath = Path.Combine(_tempDir, "same.txt");
        const string content = "hello world";
        await File.WriteAllTextAsync(filePath, content);

        // Simulate: same file was synced with the same hash before
        var hash = System.Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        _syncStateStore
            .Setup(s => s.GetContentHashAsync("FileSystem", filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hash);

        var result = await Build().SyncAsync();

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.FilesProcessed.Should().Be(0);
    }

    [Test]
    public async Task SyncAsync_WhenFileContentChanged_ShouldReIngest()
    {
        var filePath = Path.Combine(_tempDir, "changed.txt");
        const string newContent = "updated content";
        await File.WriteAllTextAsync(filePath, newContent);

        // Simulate: same path was synced but with a different (old) hash
        _syncStateStore
            .Setup(s => s.GetContentHashAsync("FileSystem", filePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync("oldhashvalue");

        var result = await Build().SyncAsync();

        _documentIngestor.Verify(d => d.IngestAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<CancellationToken>()),
            Times.Once);
        result.FilesProcessed.Should().Be(1);
    }
}
