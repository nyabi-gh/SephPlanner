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

    /// <summary>
    /// 추천을 껐다 켜는 것은 조언 쪽 이야기다. 가방이 그대로면 배치는 이미 답을 알고 있으므로
    /// 다시 풀지 않고 조언만 새로 붙는다.
    /// </summary>
    [Fact]
    public void RecommendationChangeSolvesOnlyTheAdvice()
    {
        var builds = 0;
        var advices = 0;
        PlanBuildOperation build = (
            GameSnapshot _, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
        {
            blocker = PlanBlocker.None;
            Interlocked.Increment(ref builds);
            return new Plan();
        };
        PlanAdviceOperation advise = (
            Plan placement, GameSnapshot _, ICatalog _, PlanPreferences _,
            LayoutCache _2, CancellationToken _3) =>
        {
            Interlocked.Increment(ref advices);
            return placement;
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero, advise);

        runner.Submit(Snapshot(1), new PlanPreferences { Recommendations = true }, "catalog");
        Assert.True(SpinWait.SpinUntil(
            () => runner.State.IsCurrent && runner.State.AdviceIsCurrent, TimeSpan.FromSeconds(5)));

        runner.Submit(Snapshot(1), new PlanPreferences { Recommendations = false }, "catalog");
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref advices) == 2, TimeSpan.FromSeconds(5)));

        Assert.Equal(1, Volatile.Read(ref builds));
        Assert.Equal(1, runner.State.RequestedGeneration);
        Assert.True(runner.State.IsCurrent);
    }

    /// <summary>
    /// 세피라이트 보상 창은 여닫을 때마다 후보를 6개 넣었다 비운다. 그때마다 배치까지 버리던 것이
    /// 제보 <c>4c1efa35</c> 의 "F10 을 눌렀는데 계산이 안 끝난다" 였다.
    /// </summary>
    [Fact]
    public void AnOfferChangeKeepsThePlacementPublished()
    {
        var builds = 0;
        var advices = 0;
        PlanBuildOperation build = (
            GameSnapshot _, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
        {
            blocker = PlanBlocker.None;
            Interlocked.Increment(ref builds);
            return new Plan();
        };
        PlanAdviceOperation advise = (
            Plan placement, GameSnapshot _, ICatalog _, PlanPreferences _,
            LayoutCache _2, CancellationToken _3) =>
        {
            Interlocked.Increment(ref advices);
            return placement;
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero, advise);

        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(SpinWait.SpinUntil(
            () => runner.State.IsCurrent && runner.State.AdviceIsCurrent, TimeSpan.FromSeconds(5)));
        var placement = runner.State.Latest;

        runner.Submit(WithOffer(Snapshot(1)), PlanPreferences.None, "catalog");
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref advices) == 2, TimeSpan.FromSeconds(5)));

        // 배치는 그대로 게시된 채이고 세대도 그대로다. 화면에 "갱신 중" 이 붙지 않는다.
        Assert.Equal(1, Volatile.Read(ref builds));
        Assert.True(runner.State.IsCurrent);
        Assert.Same(placement, runner.State.Latest);
        Assert.Equal(1, runner.State.RequestedGeneration);
    }

    /// <summary>배치가 바뀌면 그 배치에 붙이려던 조언은 남의 판에 대한 답이 된다.</summary>
    [Fact]
    public void APlacementChangeDropsTheAdviceInFlight()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var builds = 0;
        var advices = 0;
        PlanBuildOperation build = (
            GameSnapshot _, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
        {
            blocker = PlanBlocker.None;
            Interlocked.Increment(ref builds);
            return new Plan();
        };
        Plan? stale = null;
        PlanAdviceOperation advise = (
            Plan placement, GameSnapshot _, ICatalog _, PlanPreferences _,
            LayoutCache _2, CancellationToken cancellation) =>
        {
            if (Interlocked.Increment(ref advices) == 1)
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
                Assert.True(cancellation.IsCancellationRequested, "낡아진 조언이 멈추라는 신호를 받지 못했다");
                stale = new Plan { RequestGeneration = placement.RequestGeneration };
                return stale;
            }
            return placement;
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero, advise);

        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(started.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));

        runner.Submit(Snapshot(2), PlanPreferences.None, "catalog");
        release.Set();

        Assert.True(SpinWait.SpinUntil(
            () => runner.State.IsCurrent && runner.State.AdviceIsCurrent && Volatile.Read(ref advices) == 2,
            TimeSpan.FromSeconds(5)));

        // 배치가 먼저 돌고, 옛 배치의 조언은 게시되지 않는다.
        Assert.Equal(2, Volatile.Read(ref builds));
        Assert.NotSame(stale, runner.State.Latest);
        Assert.Equal(2, runner.State.RequestedGeneration);
        Assert.Equal(2, runner.State.Latest!.RequestGeneration);
    }

    /// <summary>조언이 실패해도 배치는 살아 있다. 그것까지 잃으면 F8 이 막힌다.</summary>
    [Fact]
    public void AFailedAdviceLeavesThePlacementAlone()
    {
        PlanBuildOperation build = (
            GameSnapshot _, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
        {
            blocker = PlanBlocker.None;
            return new Plan();
        };
        PlanAdviceOperation advise = (
            Plan _, GameSnapshot _1, ICatalog _2, PlanPreferences _3,
            LayoutCache _4, CancellationToken _5) => throw new InvalidOperationException("조언 실패");
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero, advise);

        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(SpinWait.SpinUntil(
            () => runner.State.AdviceError is not null, TimeSpan.FromSeconds(5)));

        var state = runner.State;
        Assert.Null(state.Error);
        Assert.True(state.IsCurrent);
        Assert.False(state.AdviceIsCurrent);
        Assert.NotNull(state.Latest);
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

    /// <summary>
    /// 밀림은 풀이가 느려서가 아니라 요청이 답보다 자주 와서 생긴다. 그것을 인게임에서 읽으려면
    /// 버린 풀이와 밀린 세대가 수로 남아야 한다.
    /// </summary>
    [Fact]
    public void StatsCountDiscardedSolvesAndTheBacklog()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        PlanBuildOperation build = (
            GameSnapshot _, ICatalog _, PlanPreferences _,
            out PlanBlocker blocker, Plan? _, LayoutCache _2, CancellationToken _3) =>
        {
            blocker = PlanBlocker.None;
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            return new Plan();
        };
        var runner = new PlanRunner(EmptyCatalog, build, TimeSpan.Zero);

        runner.Submit(Snapshot(1), PlanPreferences.None, "catalog");
        Assert.True(started.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
        runner.Submit(Snapshot(2), PlanPreferences.None, "catalog");
        Assert.Equal(2, runner.Stats.Backlog);

        release.Set();
        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(5)));

        var stats = runner.Stats;
        Assert.Equal(2, stats.Placement.Count);
        Assert.Equal(1, stats.Placement.Discarded);
        Assert.Equal(1, stats.Published);
        Assert.Equal(0, stats.Backlog);
        Assert.Equal(2, stats.WorstBacklog);

        // 첫 풀이가 신호를 기다린 시간이 그대로 잡히므로 둘 다 0 보다 크다.
        Assert.True(stats.Placement.WorstMs > 0);
        Assert.Equal(1, stats.Placement.WorstRun);
        Assert.True(stats.WorstPublishDelayMs > 0);
        Assert.Equal(1, stats.WorstPublishRun);

        // 게시된 배치마다 조언이 한 번 뒤따른다.
        Assert.True(SpinWait.SpinUntil(() => runner.Stats.Advice.Count == 1, TimeSpan.FromSeconds(5)));
    }

    private static GameSnapshot Snapshot(int storage) => new()
    {
        Inventory = new InventoryState { Width = 6, Height = 7, Storage = storage },
    };

    /// <summary>후보만 달라진 판. 배치 지문은 그대로이고 전체 지문만 바뀐다.</summary>
    private static GameSnapshot WithOffer(GameSnapshot snapshot)
    {
        snapshot.Offers.Add(new OfferedItem { Kind = "charm", DefinitionId = 4, Price = 1 });
        return snapshot;
    }
}
