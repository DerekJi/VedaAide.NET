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

    private static (Mock<IEvalDatasetProvider> db, Mock<IEvalDatasetProvider> hf) CreateProviderMocks()
    {
        var db = new Mock<IEvalDatasetProvider>();
        db.Setup(p => p.Supports(EvalDatasetSource.Database)).Returns(true);
        db.Setup(p => p.LoadAsync(It.IsAny<EvalDatasetSource>(), It.IsAny<EvalDatasetConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EvalQuestion { Question = "DB-Q", ExpectedAnswer = "A" }]);

        var hf = new Mock<IEvalDatasetProvider>();
        hf.Setup(p => p.Supports(EvalDatasetSource.HuggingFace)).Returns(true);
        hf.Setup(p => p.LoadAsync(It.IsAny<EvalDatasetSource>(), It.IsAny<EvalDatasetConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EvalQuestion { Question = "HF-Q", ExpectedAnswer = "A" }]);

        return (db, hf);
    }

    /// <summary>
    /// Builds a container mirroring the production wiring: leaf providers self-register via
    /// TryAddEnumerable, and the router is registered under its own interface (never as a leaf provider).
    /// </summary>
    private static (
        ServiceProvider sp,
        Mock<IEvalDatasetProvider> db,
        Mock<IEvalDatasetProvider> hf) BuildContainer()
    {
        var (db, hf) = CreateProviderMocks();

        var services = new ServiceCollection();
        services.AddScoped<IEvalDatasetProvider>(_ => db.Object);
        services.AddScoped<IEvalDatasetProvider>(_ => hf.Object);
        services.AddScoped<IEvalDatasetSourceRouter, EvalDatasetProviderDispatcher>();

        return (services.BuildServiceProvider(), db, hf);
    }

    [Test]
    public async Task LoadAsync_DispatchesToProviderThatSupportsTheSource()
    {
        var (sp, db, hf) = BuildContainer();
        var router = sp.GetRequiredService<IEvalDatasetSourceRouter>();

        var result = await router.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig());

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
        var router = sp.GetRequiredService<IEvalDatasetSourceRouter>();
        using var cts = new CancellationTokenSource();
        var config = new EvalDatasetConfig { RepoId = "ragas-v1/code-generated", MaxRecords = 5 };

        await router.LoadAsync(EvalDatasetSource.Database, config, cts.Token);

        db.Verify(p => p.LoadAsync(EvalDatasetSource.Database, config, cts.Token), Times.Once);
    }

    [Test]
    public async Task LoadAsync_NoProviderSupportsSource_ThrowsUnsupportedEvalDatasetSourceException()
    {
        var (sp, _, _) = BuildContainer();
        var router = sp.GetRequiredService<IEvalDatasetSourceRouter>();

        var act = () => router.LoadAsync(EvalDatasetSource.LocalFile, new EvalDatasetConfig());

        await act.Should().ThrowAsync<UnsupportedEvalDatasetSourceException>()
            .WithMessage("*LocalFile*");
    }

    [Test]
    public async Task LoadAsync_NoProvidersRegistered_ThrowsWithCleanMessage()
    {
        var services = new ServiceCollection();
        services.AddScoped<IEvalDatasetSourceRouter, EvalDatasetProviderDispatcher>();
        using var sp = services.BuildServiceProvider();
        var router = sp.GetRequiredService<IEvalDatasetSourceRouter>();

        var act = () => router.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig());

        await act.Should().ThrowAsync<UnsupportedEvalDatasetSourceException>()
            .WithMessage("*providers are registered*");
    }

    [Test]
    public void LeafProviderEnumerable_ContainsOnlyRegisteredProviders()
    {
        var (sp, db, hf) = BuildContainer();

        var providers = sp.GetServices<IEvalDatasetProvider>().ToArray();

        // The router is registered under its own interface, so the leaf-provider enumerable contains
        // exactly the registered providers (and can never contain the dispatcher).
        providers.Should().HaveCount(2);
        providers.Should().Contain(db.Object);
        providers.Should().Contain(hf.Object);
    }

    [Test]
    public void ContainerWiring_SingleResolve_ReturnsDispatcher()
    {
        var (sp, _, _) = BuildContainer();

        var resolved = sp.GetRequiredService<IEvalDatasetSourceRouter>();

        resolved.Should().BeOfType<EvalDatasetProviderDispatcher>();
    }

    [Test]
    public async Task ContainerWiring_OrderIndependent_RouterWorksWhenRegisteredFirst()
    {
        // Regression test for the DI-ordering issue: the router must dispatch correctly even when it is
        // registered BEFORE the leaf providers (the old design silently broke if registration order changed).
        var (db, hf) = CreateProviderMocks();

        var services = new ServiceCollection();
        services.AddScoped<IEvalDatasetSourceRouter, EvalDatasetProviderDispatcher>();
        services.AddScoped<IEvalDatasetProvider>(_ => db.Object);
        services.AddScoped<IEvalDatasetProvider>(_ => hf.Object);
        using var sp = services.BuildServiceProvider();

        var router = sp.GetRequiredService<IEvalDatasetSourceRouter>();
        var result = await router.LoadAsync(EvalDatasetSource.HuggingFace, new EvalDatasetConfig());

        result.Should().ContainSingle(q => q.Question == "HF-Q");
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
        services.AddScoped<IEvalDatasetSourceRouter, EvalDatasetProviderDispatcher>();
        using var sp = services.BuildServiceProvider();

        var router = sp.GetRequiredService<IEvalDatasetSourceRouter>();
        var result = await router.LoadAsync(EvalDatasetSource.Database, new EvalDatasetConfig { MaxRecords = 1 });

        result.Should().ContainSingle();
        repo.Verify(r => r.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
