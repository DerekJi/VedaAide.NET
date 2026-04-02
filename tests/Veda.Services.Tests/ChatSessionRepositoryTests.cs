using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Veda.Core;
using Veda.Storage;

namespace Veda.Services.Tests;

/// <summary>
/// 集成测试：ChatSessionRepository（SQLite in-memory）。
/// 验证用户隔离、CRUD 正确性及越权访问防护。
/// </summary>
[TestFixture]
public class ChatSessionRepositoryTests
{
    private SqliteConnection _connection = null!;
    private VedaDbContext _db = null!;
    private ChatSessionRepository _sut = null!;

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

        _sut = new ChatSessionRepository(_db);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Test]
    public async Task CreateAsync_ShouldReturnSessionWithGeneratedId()
    {
        var session = await _sut.CreateAsync("user-1", "My Chat");

        session.SessionId.Should().NotBeNullOrWhiteSpace();
        session.UserId.Should().Be("user-1");
        session.Title.Should().Be("My Chat");
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task CreateAsync_EmptyTitle_ShouldDefaultToNewChat()
    {
        var session = await _sut.CreateAsync("user-1", "");
        session.Title.Should().Be("New Chat");
    }

    // ── ListAsync ────────────────────────────────────────────────────────────

    [Test]
    public async Task ListAsync_ShouldOnlyReturnOwnSessions()
    {
        await _sut.CreateAsync("user-A", "Session A1");
        await _sut.CreateAsync("user-A", "Session A2");
        await _sut.CreateAsync("user-B", "Session B1");

        var sessionsA = await _sut.ListAsync("user-A");
        var sessionsB = await _sut.ListAsync("user-B");

        sessionsA.Should().HaveCount(2);
        sessionsA.Should().AllSatisfy(s => s.UserId.Should().Be("user-A"));

        sessionsB.Should().HaveCount(1);
        sessionsB[0].Title.Should().Be("Session B1");
    }

    [Test]
    public async Task ListAsync_ShouldReturnOrderedByUpdatedAtDescending()
    {
        var s1 = await _sut.CreateAsync("user-1", "First");
        await Task.Delay(5);
        var s2 = await _sut.CreateAsync("user-1", "Second");

        var list = await _sut.ListAsync("user-1");

        list[0].SessionId.Should().Be(s2.SessionId);
        list[1].SessionId.Should().Be(s1.SessionId);
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────────

    [Test]
    public async Task DeleteAsync_OwnSession_ShouldRemoveIt()
    {
        var session = await _sut.CreateAsync("user-1", "To delete");
        await _sut.DeleteAsync(session.SessionId, "user-1");

        var list = await _sut.ListAsync("user-1");
        list.Should().BeEmpty();
    }

    [Test]
    public async Task DeleteAsync_NonExistentSession_ShouldBeIdempotent()
    {
        // Should not throw
        await _sut.Awaiting(r => r.DeleteAsync("non-existent-id", "user-1"))
                  .Should().NotThrowAsync();
    }

    [Test]
    public async Task DeleteAsync_OtherUsersSession_ShouldThrowUnauthorized()
    {
        var session = await _sut.CreateAsync("user-A", "Private");

        await _sut.Awaiting(r => r.DeleteAsync(session.SessionId, "user-B"))
                  .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Test]
    public async Task DeleteAsync_ShouldCascadeDeleteMessages()
    {
        var session = await _sut.CreateAsync("user-1", "With messages");
        await _sut.AppendMessageAsync(new ChatMessageRecord(
            Guid.NewGuid().ToString(), session.SessionId, "user-1",
            "user", "Hello", null, false, [], DateTimeOffset.UtcNow));

        await _sut.DeleteAsync(session.SessionId, "user-1");

        var msgCount = await _db.ChatMessages.CountAsync();
        msgCount.Should().Be(0);
    }

    // ── AppendMessageAsync ───────────────────────────────────────────────────

    [Test]
    public async Task AppendMessageAsync_ValidSession_ShouldPersistMessage()
    {
        var session = await _sut.CreateAsync("user-1", "Chat");
        var msg = new ChatMessageRecord(
            Guid.NewGuid().ToString(), session.SessionId, "user-1",
            "user", "Hello world", 0.9f, false, [], DateTimeOffset.UtcNow);

        await _sut.AppendMessageAsync(msg);

        var messages = await _sut.GetMessagesAsync(session.SessionId, "user-1");
        messages.Should().HaveCount(1);
        messages[0].Content.Should().Be("Hello world");
        messages[0].Role.Should().Be("user");
    }

    [Test]
    public async Task AppendMessageAsync_ContentOver10000Chars_ShouldTruncate()
    {
        var session = await _sut.CreateAsync("user-1", "Chat");
        var longContent = new string('x', 12_000);
        var msg = new ChatMessageRecord(
            Guid.NewGuid().ToString(), session.SessionId, "user-1",
            "assistant", longContent, null, false, [], DateTimeOffset.UtcNow);

        await _sut.AppendMessageAsync(msg);

        var messages = await _sut.GetMessagesAsync(session.SessionId, "user-1");
        messages[0].Content.Length.Should().Be(10_000);
    }

    [Test]
    public async Task AppendMessageAsync_WrongUser_ShouldThrowUnauthorized()
    {
        var session = await _sut.CreateAsync("user-A", "Chat");
        var msg = new ChatMessageRecord(
            Guid.NewGuid().ToString(), session.SessionId, "user-B",
            "user", "Hijack", null, false, [], DateTimeOffset.UtcNow);

        await _sut.Awaiting(r => r.AppendMessageAsync(msg))
                  .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ── GetMessagesAsync ─────────────────────────────────────────────────────

    [Test]
    public async Task GetMessagesAsync_ShouldReturnMessagesOrderedByCreatedAt()
    {
        var session = await _sut.CreateAsync("user-1", "Chat");

        var t0 = DateTimeOffset.UtcNow;
        await _sut.AppendMessageAsync(new ChatMessageRecord(
            Guid.NewGuid().ToString(), session.SessionId, "user-1",
            "user", "First", null, false, [], t0));
        await _sut.AppendMessageAsync(new ChatMessageRecord(
            Guid.NewGuid().ToString(), session.SessionId, "user-1",
            "assistant", "Second", 0.85f, false, [], t0.AddSeconds(1)));

        var messages = await _sut.GetMessagesAsync(session.SessionId, "user-1");

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be("user");
        messages[1].Role.Should().Be("assistant");
    }

    [Test]
    public async Task GetMessagesAsync_WrongUser_ShouldThrowUnauthorized()
    {
        var session = await _sut.CreateAsync("user-A", "Chat");

        await _sut.Awaiting(r => r.GetMessagesAsync(session.SessionId, "user-B"))
                  .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Test]
    public async Task GetMessagesAsync_WithSources_ShouldDeserializeSources()
    {
        var session = await _sut.CreateAsync("user-1", "Chat");
        var sources = new List<ChatSourceRef>
        {
            new("doc.pdf", "Some chunk content", 0.92f, "chunk-1", "doc-1")
        };
        var msg = new ChatMessageRecord(
            Guid.NewGuid().ToString(), session.SessionId, "user-1",
            "assistant", "Answer", 0.9f, false, sources, DateTimeOffset.UtcNow);

        await _sut.AppendMessageAsync(msg);

        var messages = await _sut.GetMessagesAsync(session.SessionId, "user-1");
        messages[0].Sources.Should().HaveCount(1);
        messages[0].Sources[0].DocumentName.Should().Be("doc.pdf");
        messages[0].Sources[0].Similarity.Should().BeApproximately(0.92f, 1e-5f);
    }
}
