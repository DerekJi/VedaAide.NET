# 测试命名规范与编写约定

## 1. 测试方法命名

格式：`方法名_场景描述_ShouldAction`

```csharp
// ✅ 正确
[Test]
public void CosineSimilarity_IdenticalVectors_ShouldReturnOne() { }

[Test]
public void Process_EmptyContent_ShouldThrowArgumentException() { }

[TestCase(DocumentType.BillInvoice)]
[TestCase(DocumentType.Other)]
public void ForDocumentType_ReportAndOther_ShouldReturn512TokenSize(DocumentType type) { }

// ❌ 错误（无法快速理解测试意图）
[Test]
public void Test1() { }

[Test]
public void CosineSimilarity_Works() { }
```

## 2. 测试类命名

格式：`{被测类名}Tests`，与被测类同命名空间（但在 `Tests` 项目中）。

```csharp
// src/Veda.Core/VectorMath.cs  →  tests/Veda.Core.Tests/VectorMathTests.cs
[TestFixture]
public class VectorMathTests { }

// src/Veda.Services/QueryService.cs  →  tests/Veda.Services.Tests/QueryServiceTests.cs
[TestFixture]
public class QueryServiceTests { }
```

## 3. Arrange-Act-Assert (AAA) 结构

每个测试方法必须清晰分为三段：

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

## 4. 测试框架与断言库

- 测试框架：**NUnit**（`[TestFixture]` / `[Test]` / `[TestCase]`）
- 断言库：**FluentAssertions**（`x.Should().Be(y)`，禁止使用 `Assert.*`）
- Mock 库：**Moq**（`new Mock<T>()` / `.Setup()` / `.Verify()`）

```csharp
// ✅ FluentAssertions
result.Should().Be(1f);
result.Should().BeApproximately(1f, precision: 1e-5f);
chunks.Should().HaveCount(3);
chunks.Should().AllSatisfy(c => c.DocumentId.Should().Be(docId));
act.Should().Throw<ArgumentException>();
await act.Should().ThrowAsync<ArgumentException>();

// ❌ 不要使用 Assert（xUnit/NUnit 原生）
Assert.Equal(1, result);
Assert.Single(chunks);
```

## 5. SetUp / TearDown

使用 NUnit 的 `[SetUp]` / `[TearDown]` 代替 xUnit 构造函数和 `IAsyncLifetime`：

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

// 异步 SetUp/TearDown（集成测试）
[TestFixture]
public class SqliteVectorStoreIntegrationTests
{
    private SqliteConnection _connection = null!;

    [SetUp]
    public async Task SetUp() { /* 初始化 in-memory DB */ }

    [TearDown]
    public async Task TearDown() { /* 释放连接 */ }
}
```

## 6. Mock 约定

- 所有外部依赖（`IEmbeddingService`、`IVectorStore`、`IChatService`）在单元测试中必须 Mock。
- 使用 `Moq` 库。
- AI 输出（LLM 回答）使用固定字符串 Mock，保证测试确定性，不依赖真实网络。

```csharp
// ✅ Mock LLM，确保 CI 稳定
mockChatService.Setup(s => s.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("Mocked answer.");

// ❌ 不要在单元测试中调用真实 Ollama
```

## 7. 集成测试约定

- 使用 SQLite in-memory 模式（`DataSource=:memory:`），通过保持 `SqliteConnection` 开启来维持数据库状态。
- 每个测试方法独立创建/销毁数据库（`[SetUp]`/`[TearDown]`），避免测试间状态污染。
- 不依赖真实 Ollama，向量使用固定的 `float[]`（如 `[1f, 0f, 0f]`）。

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

## 8. 禁止事项

| 禁止 | 原因 |
|---|---|
| 在单元测试中调用真实 Ollama / LLM API | 测试不稳定、有网络依赖、成本高 |
| 多个测试共享同一个数据库实例 | 测试间状态污染，导致不确定性 |
| 在测试中硬编码绝对路径 | 破坏跨机器/CI 可移植性 |
| `Thread.Sleep` 代替 `await Task.Delay` | 阻塞线程，降低测试效率 |
| 使用 `Assert.*`（xUnit/NUnit 原生断言） | 统一使用 FluentAssertions，增强可读性 |
| 测试中使用 `[Test]` 但实际需要 `[TestCase]` | 测试覆盖不完整 |
