using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using NUnit.Framework;
using Veda.Core;
using Veda.Core.Interfaces;
using Veda.Services;

namespace Veda.Evaluation.Tests;

[TestFixture]
public class EvalDatasetProviderDispatcherTests
{
    private static List<EvalQuestion> Questions(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new EvalQuestion { Question = $"Q{i}", ExpectedAnswer = $"A{i}" })
            .ToList();

    /// <summary>
    /// Builds a container mirroring the production wiring: concrete providers self-register,
    /// and the dispatcher is registered last as the single IEvalDatasetProvider the runner sees.
    /// </summary>
    private static (
        ServiceProvider sp,
        Mock<IEvalDatasetProvider> db,
        Mock<IEvalDatasetProvider> hf) BuildContainer()
    {
        var db = new Mock<IEvalDatasetProvider>();
        db.Setup(p => p.Supports(EvalDatasetSource.Database)).Returns(true);
        db.Setup(p => p.LoadAsync(It.IsAny<EvalDatasetSource>(), It.IsAny<EvalDatasetConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EvalQuestion { Question = "DB-Q", ExpectedAnswer = "A" }]);

        var hf = new Mock<IEvalDatasetProvider>();
        hf.Setup(p => p.Supports(EvalDatasetSource.HuggingFace)).Returns(true);
        hf.Setup(p => p.LoadAsync(It.IsAny<EvalDatasetSource>(), It.IsAny<EvalDatasetConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EvalQuestion { Question = "HF-Q", ExpectedAnswer = "A" }]);

        var services = new ServiceCollection();
        services.AddScoped<IEvalDatasetProvider>(_ => db.Object);
        services.AddScoped<IEvalDatasetProvider>(_ => hf.Object);
        services.AddScoped<IEvalDatasetProvider>(sp => new EvalDatasetProviderDispatcher(sp));

        return (services.BuildServiceProvider(), db, hf);
    }

    [Test]
    public async Task LoadAsync_DispatchesToProviderThatSupportsTheSource()
    {
        var (sp, db, hf) = BuildContainer();
        var dispatcher = sp.GetRequiredService<IEvalDatasetProvider>();

        var result = await dispatcher.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig());

        result.Should().ContainSingle(q => q.Question == "DB-Q");
        db.Verify(p => p.LoadAsync(
            EvalDatasetSource.Database,
            It.IsAny<EvalDatasetConfig?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        hf.Verify(p => p.LoadAsync(
            It.IsAny<EvalDatasetSource>(),
            It.IsAny<EvalDatasetConfig?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task LoadAsync_ForwardsConfigAndCancellationToken()
    {
        var (sp, db, _) = BuildContainer();
        var dispatcher = sp.GetRequiredService<IEvalDatasetProvider>();
        using var cts = new CancellationTokenSource();
        var config = new EvalDatasetConfig { RepoId = "ragas-v1/code-generated", MaxRecords = 5 };

        await dispatcher.LoadAsync(EvalDatasetSource.Database, config, cts.Token);

        db.Verify(p => p.LoadAsync(EvalDatasetSource.Database, config, cts.Token), Times.Once);
    }

    [Test]
    public async Task LoadAsync_NoProviderSupportsSource_ThrowsNotSupportedException()
    {
        var (sp, _, _) = BuildContainer();
        var dispatcher = sp.GetRequiredService<IEvalDatasetProvider>();

        var act = () => dispatcher.LoadAsync(EvalDatasetSource.LocalFile, new EvalDatasetConfig());

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*LocalFile*");
    }

    [Test]
    public async Task LoadAsync_NoProvidersRegistered_ThrowsWithSetupGuidance()
    {
        var services = new ServiceCollection();
        services.AddScoped<IEvalDatasetProvider>(sp => new EvalDatasetProviderDispatcher(sp));
        using var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IEvalDatasetProvider>();

        var act = () => dispatcher.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig());

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*AddVedaAiServices*");
    }

    [Test]
    public void Supports_AlwaysFalse_DispatcherIsNotALeafProvider()
    {
        var (sp, _, _) = BuildContainer();
        var dispatcher = sp.GetRequiredService<IEvalDatasetProvider>();

        dispatcher.Supports(EvalDatasetSource.Database).Should().BeFalse();
        dispatcher.Supports(EvalDatasetSource.HuggingFace).Should().BeFalse();
    }

    [Test]
    public void ContainerWiring_SingleResolve_ReturnsDispatcher()
    {
        var (sp, _, _) = BuildContainer();

        var resolved = sp.GetRequiredService<IEvalDatasetProvider>();

        resolved.Should().BeOfType<EvalDatasetProviderDispatcher>();
    }

    [Test]
    public async Task ContainerWiring_WithRealDatabaseProvider_DispatchesToRepository()
    {
        var repo = new Mock<IEvalDatasetRepository>();
        repo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Questions(2));

        var services = new ServiceCollection();
        services.AddScoped(_ => repo.Object);
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IEvalDatasetProvider, DatabaseEvalDatasetProvider>());
        services.AddScoped<IEvalDatasetProvider>(sp => new EvalDatasetProviderDispatcher(sp));
        using var sp = services.BuildServiceProvider();

        var dispatcher = sp.GetRequiredService<IEvalDatasetProvider>();
        var result = await dispatcher.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig { MaxRecords = 1 });

        result.Should().ContainSingle();
        repo.Verify(r => r.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
