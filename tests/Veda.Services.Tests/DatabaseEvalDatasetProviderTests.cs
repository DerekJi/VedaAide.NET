using FluentAssertions;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;

namespace Veda.Services.Tests;

[TestFixture]
public class DatabaseEvalDatasetProviderTests
{
    private Mock<IEvalDatasetRepository> _repo = null!;
    private DatabaseEvalDatasetProvider  _sut  = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = new Mock<IEvalDatasetRepository>();
        _sut  = new DatabaseEvalDatasetProvider(_repo.Object);
    }

    private static List<EvalQuestion> Questions(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new EvalQuestion { Question = $"Q{i}", ExpectedAnswer = $"A{i}" })
            .ToList();

    [Test]
    public void Supports_Database_ReturnsTrue() =>
        _sut.Supports(EvalDatasetSource.Database).Should().BeTrue();

    [TestCase(EvalDatasetSource.HuggingFace)]
    [TestCase(EvalDatasetSource.LocalFile)]
    public void Supports_OtherSources_ReturnsFalse(EvalDatasetSource source) =>
        _sut.Supports(source).Should().BeFalse();

    [Test]
    public async Task LoadAsync_DatabaseSource_DelegatesToRepository()
    {
        var questions = Questions(2);
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(questions);

        var result = await _sut.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig());

        result.Should().BeEquivalentTo(questions);
        _repo.Verify(r => r.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LoadAsync_NullConfig_UsesProviderDefaults()
    {
        var questions = Questions(2);
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(questions);

        var result = await _sut.LoadAsync(EvalDatasetSource.Database, config: null);

        result.Should().BeEquivalentTo(questions);
    }

    [Test]
    public async Task LoadAsync_DatabaseSource_AppliesMaxRecords()
    {
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Questions(5));

        var result = await _sut.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig { MaxRecords = 3 });

        result.Should().HaveCount(3);
        result.Select(q => q.Question).Should().Equal("Q1", "Q2", "Q3");
    }

    [Test]
    public async Task LoadAsync_MaxRecordsGreaterThanAvailable_ReturnsAll()
    {
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Questions(5));

        var result = await _sut.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig { MaxRecords = 10 });

        result.Should().HaveCount(5);
    }

    [Test]
    public async Task LoadAsync_NegativeMaxRecords_ThrowsArgumentOutOfRange()
    {
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Questions(5));

        var act = () => _sut.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig { MaxRecords = -1 });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*MaxRecords*");
        _repo.Verify(r => r.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task LoadAsync_ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await _sut.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig(), cts.Token);

        _repo.Verify(r => r.ListAsync(cts.Token), Times.Once);
    }

    [TestCase(EvalDatasetSource.HuggingFace)]
    [TestCase(EvalDatasetSource.LocalFile)]
    public async Task LoadAsync_UnsupportedSource_ThrowsUnsupportedEvalDatasetSourceException(EvalDatasetSource source)
    {
        _repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var act = () => _sut.LoadAsync(source, new EvalDatasetConfig());

        await act.Should().ThrowAsync<UnsupportedEvalDatasetSourceException>();
        _repo.Verify(r => r.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
