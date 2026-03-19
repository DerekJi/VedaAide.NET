using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Veda.Core;
using Veda.Storage;

namespace Veda.Services.Tests;

/// <summary>
/// 集成测试：直接操作真实的 SQLite in-memory 数据库，不使用 Mock。
/// 使用 [SetUp]/[TearDown] 管理连接生命周期，保证 in-memory DB 在测试期间保持连接。
/// </summary>
[TestFixture]
public class SqliteVectorStoreIntegrationTests
{
    private SqliteConnection _connection = null!;
    private VedaDbContext _db = null!;
    private SqliteVectorStore _sut = null!;

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<VedaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new VedaDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _sut = new SqliteVectorStore(_db);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task UpsertBatchAsync_ValidChunks_ShouldStoreAndRetrieveBySearch()
    {
        // Arrange: identical vector → cosine = 1.0
        var queryVec = new float[] { 1f, 0f, 0f };
        var chunk = MakeChunk("doc-1", "hello world", new float[] { 1f, 0f, 0f });

        // Act
        await _sut.UpsertBatchAsync([chunk]);
        var results = await _sut.SearchAsync(queryVec, topK: 5, minSimilarity: 0.9f);

        // Assert
        results.Should().HaveCount(1);
        results[0].Chunk.Content.Should().Be("hello world");
        results[0].Similarity.Should().BeApproximately(1f, precision: 1e-5f);
    }

    [Test]
    public async Task UpsertBatchAsync_DuplicateContent_ShouldSkipDuplicate()
    {
        // Arrange
        var chunk = MakeChunk("doc-1", "unique content here", new float[] { 1f, 0f, 0f });
        await _sut.UpsertBatchAsync([chunk]);

        // Act: same content → same SHA-256 hash → should be skipped
        var duplicate = MakeChunk("doc-1", "unique content here", new float[] { 1f, 0f, 0f });
        await _sut.UpsertBatchAsync([duplicate]);

        // Assert
        var count = await _db.VectorChunks.CountAsync();
        count.Should().Be(1);
    }

    [Test]
    public async Task SearchAsync_FilterByDocumentType_ShouldReturnOnlyMatchingType()
    {
        // Arrange
        var invoiceChunk = MakeChunk("inv-1", "invoice data", new float[] { 1f, 0f, 0f }, DocumentType.BillInvoice);
        var specChunk = MakeChunk("spec-1", "spec data", new float[] { 0.9f, 0.1f, 0f }, DocumentType.Specification);
        await _sut.UpsertBatchAsync([invoiceChunk, specChunk]);

        // Act: only BillInvoice
        var results = await _sut.SearchAsync(
            new float[] { 1f, 0f, 0f },
            topK: 5,
            minSimilarity: 0.5f,
            filterType: DocumentType.BillInvoice);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r.Chunk.DocumentType.Should().Be(DocumentType.BillInvoice));
    }

    [Test]
    public async Task DeleteByDocumentAsync_ExistingDocument_ShouldRemoveAllItsChunks()
    {
        // Arrange
        const string docId = "delete-me";
        var chunks = new[]
        {
            MakeChunk("doc-del", "chunk one",  new float[] { 1f, 0f, 0f }, documentId: docId),
            MakeChunk("doc-del", "chunk two",  new float[] { 0f, 1f, 0f }, documentId: docId)
        };
        var other = MakeChunk("doc-keep", "keep this", new float[] { 0f, 0f, 1f });
        await _sut.UpsertBatchAsync(chunks);
        await _sut.UpsertBatchAsync([other]);

        // Act
        await _sut.DeleteByDocumentAsync(docId);

        // Assert: only the unrelated chunk remains
        var remaining = await _db.VectorChunks.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Content.Should().Be("keep this");
    }

    [Test]
    public async Task UpsertAsync_SingleChunk_ShouldStoreSuccessfully()
    {
        // Arrange
        var chunk = MakeChunk("doc-1", "single upsert test", new float[] { 0.5f, 0.5f, 0f });

        // Act
        await _sut.UpsertAsync(chunk);

        // Assert
        var count = await _db.VectorChunks.CountAsync();
        count.Should().Be(1);
    }

    [Test]
    public async Task SearchAsync_BelowMinSimilarityThreshold_ShouldReturnEmpty()
    {
        // Arrange: orthogonal vector → cosine = 0
        await _sut.UpsertBatchAsync([MakeChunk("doc-1", "some text", new float[] { 1f, 0f, 0f })]);

        // Act: query with orthogonal vector, threshold = 0.9
        var results = await _sut.SearchAsync(
            new float[] { 0f, 1f, 0f },
            topK: 5,
            minSimilarity: 0.9f);

        // Assert
        results.Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_WithTopKLimit_ShouldReturnAtMostTopKResults()
    {
        // Arrange: 5 chunks with distinct content (distinct SHA256 → hash dedup passes all 5 into DB)
        //          but identical query vectors so all 5 score similarity = 1.0.
        //          This test validates topK truncation in SearchAsync, not dedup behaviour.
        var chunks = Enumerable.Range(1, 5)
            .Select(i => MakeChunk($"doc-{i}", $"content {i}", new float[] { 1f, 0f, 0f }))
            .ToArray();
        await _sut.UpsertBatchAsync(chunks);

        // Act: request only top 3
        var results = await _sut.SearchAsync(
            new float[] { 1f, 0f, 0f },
            topK: 3,
            minSimilarity: 0.9f);

        // Assert: exactly topK results returned even though all 5 match
        results.Should().HaveCount(3);
    }

    [Test]
    public async Task SearchAsync_WithDateFromFilter_ShouldReturnOnlyChunksAfterDate()
    {
        // Arrange: insert old chunk, then capture a cutoff, then insert new chunk
        var oldChunk = MakeChunk("old-doc", "old content", new float[] { 1f, 0f, 0f });
        await _sut.UpsertBatchAsync([oldChunk]);

        // Small delay + capture cutoff after old chunk is stored
        await Task.Delay(50);
        var cutoff = DateTimeOffset.UtcNow;
        await Task.Delay(50);

        var newChunk = MakeChunk("new-doc", "new content", new float[] { 1f, 0f, 0f });
        await _sut.UpsertBatchAsync([newChunk]);

        // Act: search with dateFrom = cutoff (should only return new chunk)
        var results = await _sut.SearchAsync(
            new float[] { 1f, 0f, 0f },
            topK: 10,
            minSimilarity: 0.9f,
            dateFrom: cutoff);

        // Assert
        results.Should().HaveCount(1);
        results[0].Chunk.DocumentName.Should().Be("new-doc");
    }

    [Test]
    public async Task SearchAsync_WithDateToFilter_ShouldReturnOnlyChunksBeforeDate()
    {
        // Arrange: insert early chunk, capture cutoff, then insert late chunk
        var earlyChunk = MakeChunk("early-doc", "early content", new float[] { 1f, 0f, 0f });
        await _sut.UpsertBatchAsync([earlyChunk]);

        await Task.Delay(50);
        var cutoff = DateTimeOffset.UtcNow;
        await Task.Delay(50);

        var lateChunk = MakeChunk("late-doc", "late content", new float[] { 1f, 0f, 0f });
        await _sut.UpsertBatchAsync([lateChunk]);

        // Act: search with dateTo = cutoff (should only return early chunk)
        var results = await _sut.SearchAsync(
            new float[] { 1f, 0f, 0f },
            topK: 10,
            minSimilarity: 0.9f,
            dateTo: cutoff);

        // Assert
        results.Should().HaveCount(1);
        results[0].Chunk.DocumentName.Should().Be("early-doc");
    }

    private static DocumentChunk MakeChunk(
        string documentName,
        string content,
        float[] embedding,
        DocumentType type = DocumentType.Other,
        string? documentId = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        DocumentId = documentId ?? Guid.NewGuid().ToString(),
        DocumentName = documentName,
        DocumentType = type,
        Content = content,
        ChunkIndex = 0,
        Embedding = embedding,
        Metadata = new Dictionary<string, string>()
    };
}
