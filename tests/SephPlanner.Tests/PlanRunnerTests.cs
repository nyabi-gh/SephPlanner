using System.Collections.Concurrent;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public class PlanRunnerTests
{
    private static readonly Catalog EmptyCatalog = new(
        Array.Empty<TabletDefinition>(), Array.Empty<CharmDefinition>());

    [Fact]
    public void BusyRunnerKeepsOnlyTheLatestPendingRequest()
    {
        using var releaseFirst = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<int>();
        PlanBuildOperation build = (
            GameSnapshot snapshot, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _) =>
        {
            blocker = PlanBlocker.None;
            var storage = snapshot.Inventory!.Storage;
            calls.Enqueue(storage);
            if (storage == 1) releaseFirst.Wait(TimeSpan.FromSeconds(5));
            return new Plan();
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero);

        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(SpinWait.SpinUntil(() => calls.Count == 1, TimeSpan.FromSeconds(5)));
        runner.Submit(Snapshot(2), PlanPreferences.None, "catalog");
        runner.Submit(Snapshot(3), PlanPreferences.None, "catalog");
        releaseFirst.Set();

        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { 1, 3 }, calls.ToArray());
        Assert.Equal(3, runner.State.RequestedGeneration);
        Assert.Equal(3, runner.State.Latest!.RequestGeneration);
    }

    [Fact]
    public void AnOlderCompletionIsNeverPublished()
    {
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        var calls = 0;
        PlanBuildOperation build = (
            GameSnapshot snapshot, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _) =>
        {
            blocker = PlanBlocker.None;
            var call = Interlocked.Increment(ref calls);
            if (call == 1) releaseFirst.Wait(TimeSpan.FromSeconds(5));
            if (call == 2) releaseSecond.Wait(TimeSpan.FromSeconds(5));
            return new Plan();
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero);

        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, TimeSpan.FromSeconds(5)));
        runner.Submit(Snapshot(2), PlanPreferences.None, "catalog");
        releaseFirst.Set();

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 2, TimeSpan.FromSeconds(5)));
        Assert.False(runner.State.IsCurrent);
        Assert.Null(runner.State.Latest);

        releaseSecond.Set();
        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void FailureDoesNotMakeThePreviousPlanCurrentAndCanRetry()
    {
        var calls = 0;
        PlanBuildOperation build = (
            GameSnapshot snapshot, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _) =>
        {
            blocker = PlanBlocker.None;
            if (Interlocked.Increment(ref calls) == 2) throw new InvalidOperationException("broken");
            return new Plan();
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero);

        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(5)));
        var first = runner.State.Latest;

        runner.Submit(Snapshot(2), PlanPreferences.None, "catalog");
        Assert.True(SpinWait.SpinUntil(() => runner.State.Error is not null, TimeSpan.FromSeconds(5)));
        Assert.False(runner.State.IsCurrent);
        Assert.Same(first, runner.State.Latest);

        runner.Submit(Snapshot(2), PlanPreferences.None, "catalog");
        Assert.True(SpinWait.SpinUntil(
            () => runner.State.IsCurrent && runner.State.Latest != first, TimeSpan.FromSeconds(5)));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void RecommendationChangeCreatesANewGeneration()
    {
        PlanBuildOperation build = (
            GameSnapshot _, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _) =>
        {
            blocker = PlanBlocker.None;
            return new Plan();
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero);

        runner.Submit(Snapshot(1), new PlanPreferences { Recommendations = true }, "catalog");
        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(5)));
        runner.Submit(Snapshot(1), new PlanPreferences { Recommendations = false }, "catalog");

        Assert.True(SpinWait.SpinUntil(
            () => runner.State.IsCurrent && runner.State.RequestedGeneration == 2,
            TimeSpan.FromSeconds(5)));
    }

    private static GameSnapshot Snapshot(int storage) => new()
    {
        Inventory = new InventoryState { Width = 6, Height = 7, Storage = storage },
    };
}
