# Test Naming Conventions and Coding Standards

> 中文版见 [test-conventions.cn.md](test-conventions.cn.md)

## 1. Test Method Naming

Format: `MethodName_ScenarioDescription_ShouldAction`

```csharp
// ✅ Correct
[Test]
public void CosineSimilarity_IdenticalVectors_ShouldReturnOne() { }

[Test]
public void Process_EmptyContent_ShouldThrowArgumentException() { }

[TestCase(DocumentType.BillInvoice)]
[TestCase(DocumentType.Other)]
public void ForDocumentType_ReportAndOther_ShouldReturn512TokenSize(DocumentType type) { }

// ❌ Wrong (intent is not immediately clear)
[Test]
public void Test1() { }

[Test]
public void CosineSimilarity_Works() { }
```

## 2. Test Class Naming

Format: `{ClassUnderTest}Tests`, in the same namespace as the class being tested (but inside the `Tests` project).

```csharp
// src/Veda.Core/VectorMath.cs  →  tests/Veda.Core.Tests/VectorMathTests.cs
[TestFixture]
public class VectorMathTests { }

// src/Veda.Services/QueryService.cs  →  tests/Veda.Services.Tests/QueryServiceTests.cs
[TestFixture]
public class QueryServiceTests { }
```

## 3. Arrange-Act-Assert (AAA) Structure

Each test method must be clearly split into three sections:

```csharp
[Test]
public async Task IngestAsync_ValidInput_ShouldReturnCorrectChunkCount()
{
    // Arrange
    var mockProcessor = new Mock<IDocumentProcessor>();
    mockProcessor
        .Setup(p => p.Process(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DocumentType>(), It.IsAny<string>()))
        .Returns([new DocumentChunk { Content = "chunk1" }, new DocumentChunk { Content = "chunk2" }]);

    var sut = new DocumentIngestService(mockProcessor.Object, /* ... */);

    // Act
    var result = await sut.IngestAsync("some content", "doc.txt", DocumentType.Other);

    // Assert
    result.ChunksStored.Should().Be(2);
}
```

## 4. Test Framework and Assertion Library

- Test framework: **NUnit** (`[TestFixture]` / `[Test]` / `[TestCase]`)
- Assertion library: **FluentAssertions** (`x.Should().Be(y)` — do not use `Assert.*`)
- Mock library: **Moq** (`new Mock<T>()` / `.Setup()` / `.Verify()`)

```csharp
// ✅ FluentAssertions
result.Should().Be(1f);
result.Should().BeApproximately(1f, precision: 1e-5f);
chunks.Should().HaveCount(3);
chunks.Should().AllSatisfy(c => c.DocumentId.Should().Be(docId));
act.Should().Throw<ArgumentException>();
await act.Should().ThrowAsync<ArgumentException>();

// ❌ Do not use Assert (xUnit/NUnit native)
Assert.Equal(1, result);
Assert.Single(chunks);
```

## 5. SetUp / TearDown

Use NUnit's `[SetUp]` / `[TearDown]` instead of xUnit constructors and `IAsyncLifetime`:

```csharp
[TestFixture]
public class QueryServiceTests
{
    private Mock<IEmbeddingService> _embedding = null!;
    private QueryService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _embedding = new Mock<IEmbeddingService>();
        _sut = new QueryService(_embedding.Object, /* ... */);
    }
}

// Async SetUp/TearDown (integration tests)
[TestFixture]
public class SqliteVectorStoreIntegrationTests
{
    private SqliteConnection _connection = null!;

    [SetUp]
    public async Task SetUp() { /* initialize in-memory DB */ }

    [TearDown]
    public async Task TearDown() { /* release connection */ }
}
```

## 6. Mock Conventions

- All external dependencies (`IEmbeddingService`, `IVectorStore`, `IChatService`) must be mocked in unit tests.
- Use the `Moq` library.
- LLM outputs use fixed strings in mocks to ensure test determinism — never call a real network endpoint.

```csharp
// ✅ Mock the LLM to keep CI stable
mockChatService.Setup(s => s.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("Mocked answer.");

// ❌ Never call real Ollama in a unit test
```

## 7. Integration Test Conventions

- Use SQLite in-memory mode (`DataSource=:memory:`), keeping the `SqliteConnection` open to maintain database state across the test.
- Each test method independently creates and destroys its database via `[SetUp]`/`[TearDown]` to avoid state leakage between tests.
- No real Ollama dependency — use fixed `float[]` vectors (e.g., `[1f, 0f, 0f]`).

```csharp
[TestFixture]
public class SqliteVectorStoreIntegrationTests
{
    private SqliteConnection _connection = null!;
    private VedaDbContext _db = null!;

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VedaDbContext>()
            .UseSqlite(_connection).Options;
        _db = new VedaDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
```

## 8. Prohibited Patterns

| Prohibited | Reason |
|---|---|
| Calling real Ollama / LLM APIs in unit tests | Tests become flaky, introduce network dependency and cost |
| Multiple tests sharing one database instance | State leakage between tests causes non-determinism |
| Hardcoded absolute paths in tests | Breaks portability across machines and CI environments |
| `Thread.Sleep` instead of `await Task.Delay` | Blocks the thread, reduces test efficiency |
| Using `Assert.*` (xUnit/NUnit native assertions) | All assertions must use FluentAssertions for consistency and readability |
| Using `[Test]` when `[TestCase]` is needed | Leads to incomplete scenario coverage |
