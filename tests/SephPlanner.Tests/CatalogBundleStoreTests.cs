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

    [Fact]
    public void InterruptedRefreshDoesNotReuseThePreviousSuccess()
    {
        Publish("first", "1", "assembly-1");
        var interrupted = CatalogBundleStore.Begin(_directory, "second", "1", "assembly-1");
        interrupted.WriteText(PlannerData.TabletDbFile, "partial");

        Assert.False(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out _, out var refreshing));
        Assert.Contains("완료되지", refreshing);

        CatalogBundleStore.MarkFailed(_directory, "second", "injected");

        Assert.False(CatalogBundleStore.TryGetActive(
            _directory, "1", "assembly-1", out _, out var failed));
        Assert.Contains("실패", failed);
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
