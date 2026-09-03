using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public class CatalogBundleStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "SephPlanner.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void OnlyACompleteVerifiedGenerationBecomesActive()
    {
        Publish("first", "1", "assembly-1");

        Assert.True(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out var active, out _));
        Assert.Equal("first", active.Generation);
        Assert.True(active.VerificationPassed);
    }

    /// <summary>
    /// 갱신은 이전 카탈로그를 죽이지 않는다. 죽이면 실패한 F9 한 번이 그 세션 내내 쓸 카탈로그를
    /// 없애고, 되살릴 길도 재시도도 없다.
    /// </summary>
    [Fact]
    public void AnInterruptedRefreshLeavesThePreviousCatalogActive()
    {
        Publish("first", "1", "assembly-1");
        var interrupted = CatalogBundleStore.Begin(_directory, "second", "1", "assembly-1");
        interrupted.WriteText(PlannerData.TabletDbFile, "partial");

        Assert.True(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out var duringRefresh, out _));
        Assert.Equal("first", duringRefresh.Generation);

        CatalogBundleStore.MarkFailed(_directory, "second", "injected");

        Assert.True(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out var afterFailure, out _));
        Assert.Equal("first", afterFailure.Generation);
    }

    [Fact]
    public void AnIncompleteGenerationNeverBecomesActive()
    {
        var interrupted = CatalogBundleStore.Begin(_directory, "first", "1", "assembly-1");
        interrupted.WriteText(PlannerData.TabletDbFile, "partial");

        Assert.False(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out _, out var error));
        Assert.Contains("활성 카탈로그가 없습니다", error);
    }

    [Fact]
    public void AFailedRefreshStaysReadableWithoutTouchingTheActiveCatalog()
    {
        Publish("first", "1", "assembly-1");
        CatalogBundleStore.Begin(_directory, "second", "1", "assembly-1");
        CatalogBundleStore.MarkFailed(_directory, "second", "injected");

        var status = CatalogBundleStore.ReadRefreshStatus(_directory, out var generation, out var reason);

        Assert.Equal(CatalogRefreshStatus.Failed, status);
        Assert.Equal("second", generation);
        Assert.Equal("injected", reason);
    }

    [Fact]
    public void PublishingRemovesTheGenerationItReplaced()
    {
        Publish("first", "1", "assembly-1");
        Publish("second", "1", "assembly-1");

        var generations = Path.Combine(_directory, PlannerData.CatalogGenerationsDirectory);

        Assert.False(Directory.Exists(Path.Combine(generations, "first")));
        Assert.True(Directory.Exists(Path.Combine(generations, "second")));
    }

    [Fact]
    public void AFileChangedAfterPublicationInvalidatesTheBundle()
    {
        Publish("first", "1", "assembly-1");
        Assert.True(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out var active, out _));

        File.AppendAllText(Path.Combine(active.Directory, PlannerData.CharmDbFile), "tampered");

        Assert.False(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out _, out var error));
        Assert.Contains(PlannerData.CharmDbFile, error);
    }

    [Fact]
    public void AGameAssemblyChangeInvalidatesTheBundle()
    {
        Publish("first", "1", "assembly-1");

        Assert.False(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-2", out _, out var error));
        Assert.Contains("어셈블리", error);
    }

    [Fact]
    public void AGameVersionChangeInvalidatesTheBundle()
    {
        Publish("first", "1", "assembly-1");

        Assert.False(CatalogBundleStore.TryGetActive(
            _directory, "2", "assembly-1", out _, out var error));
        Assert.Contains("게임 버전", error);
    }

    [Fact]
    public void AFailedVerificationPublishesItsOwnResultInsteadOfReusingAnOlderPass()
    {
        Publish("first", "1", "assembly-1");
        var writer = CatalogBundleStore.Begin(_directory, "second", "1", "assembly-1");
        foreach (var file in PlannerData.RequiredCatalogFiles)
            writer.WriteText(file, file + " second content");

        writer.Publish(comparisons: 10, mismatches: 1);

        Assert.True(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out var active, out _));
        Assert.Equal("second", active.Generation);
        Assert.False(active.VerificationPassed);
    }

    private void Publish(string generation, string gameVersion, string assembly)
    {
        var writer = CatalogBundleStore.Begin(_directory, generation, gameVersion, assembly);
        foreach (var file in PlannerData.RequiredCatalogFiles)
            writer.WriteText(file, file + " content");
        writer.Publish(comparisons: 10, mismatches: 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
