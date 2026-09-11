using System.Collections.Concurrent;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public class PlanRunnerTests
{
    private static readonly Catalog EmptyCatalog = new(
        Array.Empty<TabletDefinition>(), Array.Empty<CharmDefinition>());

    [Fact]
    public void DisposingCancelsRunningWorkAndDropsPendingWork()
    {
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var calls = 0;
        var cancelled = false;
        PlanBuildOperation build = (
            GameSnapshot _, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken cancellation) =>
        {
            blocker = PlanBlocker.None;
            Interlocked.Increment(ref calls);
            started.Set();
            finish.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
            cancelled = cancellation.IsCancellationRequested;
            return new Plan();
        };
        var runner = new PlanRunner(EmptyCatalog, build);
        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        runner.Submit(Snapshot(2), PlanPreferences.None, "catalog");
        runner.Dispose();
        runner.Dispose();
        finish.Set();
        Assert.True(SpinWait.SpinUntil(() => !runner.State.IsBusy, TimeSpan.FromSeconds(5)));
        Assert.True(cancelled);
        Assert.Equal(1, calls);
        Assert.False(runner.State.HasPending);
        Assert.False(runner.State.IsCurrent);
        Assert.Null(runner.State.Latest);
        Assert.Throws<ObjectDisposedException>(() => runner.Submit(Snapshot(3), PlanPreferences.None, "catalog"));
    }

    [Fact]
    public void BusyRunnerKeepsOnlyTheLatestPendingRequest()
    {
        using var releaseFirst = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<int>();
        PlanBuildOperation build = (
            GameSnapshot snapshot, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
        {
            blocker = PlanBlocker.None;
            var storage = snapshot.Inventory!.Storage;
            calls.Enqueue(storage);
            if (storage == 1) releaseFirst.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
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
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
        {
            blocker = PlanBlocker.None;
            var call = Interlocked.Increment(ref calls);
            if (call == 1) releaseFirst.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
            if (call == 2) releaseSecond.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
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
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
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
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
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

    /// <summary>
    /// 뒤에 요청이 오면 지금 도는 풀이의 답은 어차피 버려진다. 그것을 끝까지 돌게 두면 CPU 와
    /// 할당을 그대로 버리는 것이고, 실측에서 그 한 번이 1초에 2GB 까지 갔다.
    /// </summary>
    [Fact]
    public void ASupersededRunIsToldToStop()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var toldToStop = false;
        var calls = 0;
        PlanBuildOperation build = (
            GameSnapshot _, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken cancellation) =>
        {
            blocker = PlanBlocker.None;
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
                toldToStop = cancellation.IsCancellationRequested;
            }
            return new Plan();
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero);

        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(started.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));

        runner.Submit(Snapshot(2), PlanPreferences.None, "catalog");
        release.Set();

        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(5)));
        Assert.True(toldToStop, "낡아진 풀이가 멈추라는 신호를 받지 못했다");
        Assert.Equal(2, runner.State.RequestedGeneration);
    }

    private static GameSnapshot Snapshot(int storage) => new()
    {
        Inventory = new InventoryState { Width = 6, Height = 7, Storage = storage },
    };
}
