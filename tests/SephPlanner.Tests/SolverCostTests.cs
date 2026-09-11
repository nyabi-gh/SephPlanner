using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 풀이가 얼마나 일하는지를 고정한다.
///
/// <b>왜 이 테스트가 있는가.</b> 조언 하나의 값을 "판을 통째로 다시 푼 결과"로 정의해 두었더니,
/// 갈래가 늘어날 때마다 배치 탐색이 그만큼 다시 돌았다. 실측에서 42칸 가방이 꽉 찬 채 후보 8개를
/// 보면 탐색이 288번 돌아 <b>41초, 할당 48GB</b> 였고, 그동안 게임은 GC 에 잡혀 프레임이 무너졌다.
///
/// 시간이 아니라 <b>배치 탐색 횟수</b>로 고정하는 것은, 시간은 기계마다 달라도 이 횟수는 알고리즘의
/// 성질이라 어디서 재도 같기 때문이다. 이 수가 갈래 수를 따라 늘기 시작하면 그때가 회귀다.
/// </summary>
public class SolverCostTests
{
    private const int Tablets = 6;

    /// <summary>
    /// 가방이 꽉 찬 판. 후보를 집으려면 무엇이든 하나는 빠져야 한다.
    /// <paramref name="spare"/>를 주면 그만큼 자리가 남아 밀어내는 갈래가 생기지 않는다.
    /// </summary>
    private static PlacementProblem FullBag(int charmCount, int spare = 0)
    {
        var storage = Tablets + charmCount + spare;
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, storage) };

        var queries = new[] { "RIGHT 1", "HORIZONTAL 2", "UP 1", "O 2", "VERTICAL 1", "KNIGHTUPLEFT 2" };
        for (var i = 0; i < Tablets; i++)
        {
            problem.Tablets.Add(new TabletSlot
            {
                InstanceId = 900 + i,
                Definition = new TabletDefinition { Id = "t" + i, EntityId = 700 + i, Query = queries[i] },
            });
        }
        for (var i = 0; i < charmCount; i++)
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = i,
                Definition = new CharmDefinition { Id = "c" + i, EntityId = 200 + i, MaxLevel = 5 },
            });
        }
        return problem;
    }

    private static List<OfferCandidate> Candidates(int charms, int tablets)
    {
        var candidates = new List<OfferCandidate>();
        for (var i = 0; i < charms; i++)
        {
            candidates.Add(new OfferCandidate
            {
                Kind = "charm",
                Name = "offered-charm" + i,
                Charm = new CharmDefinition { Id = "oc" + i, EntityId = 300 + i, MaxLevel = 5, Rarity = Rarity.Rare },
            });
        }
        for (var i = 0; i < tablets; i++)
        {
            candidates.Add(new OfferCandidate
            {
                Kind = "tablet",
                Name = "offered-tablet" + i,
                Tablet = new TabletDefinition { Id = "ot" + i, EntityId = 400 + i, Query = "RIGHT 2" },
            });
        }
        return candidates;
    }

    /// <summary>
    /// 후보 추천의 배치 탐색은 <b>후보 수</b>를 따라야지 갈래 수를 따르면 안 된다.
    ///
    /// 후보 하나가 탐색을 쓰는 자리는 둘뿐이다. 석판 후보라 석판 구성이 달라질 때 한 번,
    /// 그리고 이긴 갈래가 석판을 밀어낸 갈래라 보고할 점수를 제대로 내야 할 때 한 번이다.
    /// 아티팩트를 밀어내는 갈래는 - 가방이 찼을 때 갈래의 대부분이다 - 기준 탐색을 그대로 쓴다.
    /// </summary>
    [Fact]
    public void RankingOffersSearchesLayoutsPerCandidateNotPerTrial()
    {
        var problem = FullBag(charmCount: 35);
        var candidates = Candidates(charms: 4, tablets: 4);
        var cache = new LayoutCache();

        var advice = OfferAdvisor.Rank(problem, candidates, gold: int.MaxValue, layouts: cache);

        Assert.Equal(candidates.Count, advice.Count);
        Assert.True(
            cache.Searches <= 1 + 2 * candidates.Count,
            $"후보 {candidates.Count}개(갈래는 후보마다 {problem.Charms.Count + problem.Tablets.Count}개)에 " +
            $"배치 탐색이 {cache.Searches}번 돌았다. 갈래마다 다시 푸는 옛 방식으로 돌아간 것이다.");

        // 돌려 쓰지 못했다면 위의 상한도 뜻이 없다. 갈래가 실제로 많았음을 함께 확인한다.
        Assert.True(cache.Reuses > 100, $"돌려 쓴 횟수가 {cache.Reuses}뿐이다. 갈래가 줄어든 것은 아닌지 보라.");
    }

    /// <summary>
    /// 가방에 든 것이 늘어도 탐색 횟수는 그대로여야 한다. 늘어나는 것은 "무엇을 밀어낼까"의
    /// 갈래뿐이고, 그것은 배정만 다시 풀면 되는 일이다.
    /// </summary>
    [Fact]
    public void AFullerBagDoesNotCostMoreLayoutSearches()
    {
        var small = new LayoutCache();
        OfferAdvisor.Rank(FullBag(charmCount: 10), Candidates(charms: 4, tablets: 0), int.MaxValue, layouts: small);

        var large = new LayoutCache();
        OfferAdvisor.Rank(FullBag(charmCount: 35), Candidates(charms: 4, tablets: 0), int.MaxValue, layouts: large);

        Assert.Equal(small.Searches, large.Searches);
        Assert.True(large.Reuses > small.Reuses, "갈래가 늘었으면 돌려 쓴 횟수는 늘어야 한다.");
    }

    private static Catalog MixCatalog() => new(
        new[] { new TabletDefinition { Id = "mixed", EntityId = TabletMix.ResultEntityId } },
        Array.Empty<CharmDefinition>());

    /// <summary>
    /// 후보 추천과 석판 합성은 <b>같은 기준 배치</b> 위에서 겨뤄야 한다.
    ///
    /// 둘은 탐색 강도를 저마다 적어 두고 기준을 따로 풀고 있었다. 강도가 <see cref="LayoutCache"/>의
    /// 열쇠에 들어가므로, 한쪽만 고치면 캐시가 조용히 갈라져 <b>두 조언이 서로 다른 배치를 기준으로
    /// 증가분을 말하게 된다.</b> 컴파일도 되고 테스트도 통과하는 종류의 사고라 여기서 붙잡는다.
    ///
    /// 자리를 남긴 판을 쓰는 것은 밀어내는 갈래를 없애기 위해서다. 아티팩트 후보는 석판 구성을
    /// 건드리지 않으므로, 두 조언이 같은 강도를 쓰는 한 석판 탐색은 처음 한 번이 전부다.
    /// </summary>
    [Fact]
    public void BothAdvisorsShareOneBaseLayoutSearch()
    {
        var problem = FullBag(charmCount: 12, spare: 6);
        var shared = new LayoutCache();

        TabletMixAdvisor.Rank(problem, MixCatalog(), cost: 0, gold: 1000, limit: 1, layouts: shared);
        var afterMix = shared.Searches;

        OfferAdvisor.Rank(problem, Candidates(charms: 3, tablets: 0), int.MaxValue, layouts: shared);

        Assert.Equal(afterMix, shared.Searches);
    }

    /// <summary>
    /// 기준 배치는 판과 강도가 그대로일 때만 돌려 쓴다. 강도가 다른데도 남의 답을 주면, 기준과
    /// 후보가 다른 잣대로 풀리는 것을 막으려던 자리가 도리어 그것을 만들어 낸다.
    /// </summary>
    [Fact]
    public void TheBaselineIsReusedOnlyForTheSameProblemAndStrength()
    {
        var problem = FullBag(charmCount: 12, spare: 6);
        var cache = new LayoutCache();
        var advice = SolverOptions.ForAdvice(default);

        var first = cache.Baseline(problem, advice);

        Assert.Same(first, cache.Baseline(problem, SolverOptions.ForAdvice(default)));
        Assert.NotSame(first, cache.Baseline(problem, new SolverOptions { BeamWidth = 32 }));
        Assert.NotSame(first, cache.Baseline(FullBag(charmCount: 12, spare: 6), advice));
    }

    /// <summary>
    /// 합성 추천은 쌍마다 처음부터 풀지 않는다. 짐작으로 줄을 세우고 상위 몇만 다시 푼다 -
    /// 석판 다섯이면 쌍이 열인데, 예전에는 그 열을 전부 풀어 2.3초에 3.2GB 였다.
    /// </summary>
    [Fact]
    public void RankingMixesSearchesOnlyForTheOnesItShows()
    {
        var problem = FullBag(charmCount: 20);
        var cache = new LayoutCache();

        const int limit = 5;
        var advice = TabletMixAdvisor.Rank(
            problem, MixCatalog(), cost: 0, gold: 1000, limit: limit, layouts: cache);

        Assert.NotEmpty(advice);
        Assert.True(
            cache.Searches <= 1 + limit,
            $"쌍이 {Tablets * (Tablets - 1) / 2}개인데 배치 탐색이 {cache.Searches}번 돌았다. " +
            "보여줄 것만 다시 푸는 규약이 깨졌다.");
    }

    /// <summary>
    /// <b>빔 열쇠에는 채점 강도가 없다.</b>
    ///
    /// 빔이 <c>SolverOptions</c>에서 읽는 것은 폭 셋뿐인데 열쇠가 다듬기 횟수와 탐색 예산까지
    /// 세고 있었다. 그래서 <see cref="DiscardAdvisor"/>가 <c>ForAdvice</c>의 예산 둘을 덮어쓰는
    /// 것만으로 칸이 갈려, 같은 빔을 두 번 찾았다. 열쇠를 도로 합치면 이 테스트가 잡는다.
    /// </summary>
    [Fact]
    public void TheBeamKeyIgnoresScoringStrength()
    {
        var problem = FullBag(charmCount: 12, spare: 6);
        var cache = new LayoutCache();

        cache.Of(problem, SolverOptions.ForAdvice(default));

        var cheaper = SolverOptions.ForAdvice(default);
        cheaper.EmptySideTrials = 12;
        cheaper.PriorityComboTrials = 24;
        cheaper.PolishPasses = 1;
        cheaper.FixpointIterations = 1;
        cache.Of(problem, cheaper);
        Assert.Equal(1, cache.Searches);

        // 폭은 빔이 읽는 값이다. 그쪽이 달라지면 다시 찾아야 한다.
        cache.Of(problem, new SolverOptions { BeamWidth = 32 });
        Assert.Equal(2, cache.Searches);
    }

    /// <summary>
    /// 열쇠를 나눠도 <b>잣대는 강도마다 따로</b>여야 한다. 빔은 나눠 쓰되 채점 결과는 아니라는
    /// 것이 2번에서 세운 선이고, 그 선이 이 둘 사이를 지난다.
    /// </summary>
    [Fact]
    public void TheYardstickStillSplitsByScoringStrength()
    {
        var problem = FullBag(charmCount: 12, spare: 6);
        var cache = new LayoutCache();
        var cheaper = SolverOptions.ForAdvice(default);
        cheaper.PolishPasses = 1;

        var first = cache.Yardstick(problem, SolverOptions.ForAdvice(default));

        Assert.Same(first, cache.Yardstick(problem, SolverOptions.ForAdvice(default)));
        Assert.NotSame(first, cache.Yardstick(problem, cheaper));
        Assert.Equal(1, cache.Searches);
    }

    /// <summary>
    /// 제거 조언은 제 빔을 따로 찾지 않는다.
    ///
    /// <see cref="DiscardAdvisor"/>는 <see cref="LayoutCache"/>를 받지 않고
    /// <c>PlacementSolver.SearchLayouts</c>를 직접 불렀다 - <see cref="LayoutCache.Searches"/>에
    /// 잡히지도 않는 탐색이었고, 제보 <c>3fc4d9ac</c>(석판 13)에서 재계산 <b>672ms 중 490ms</b>가
    /// 그 한 번이었다. 다른 조언이 이미 같은 강도의 빔을 찾아 두었으면 한 번도 더 찾지 않아야
    /// 하고, 다음 계획에서도 마찬가지여야 한다.
    /// </summary>
    [Fact]
    public void RankingDiscardsDoesNotSearchItsOwnBeam()
    {
        var problem = FullBag(charmCount: 35);
        var cache = new LayoutCache();
        var advice = SolverOptions.ForAdvice(default);

        OfferAdvisor.Rank(problem, Candidates(charms: 2, tablets: 0), int.MaxValue, layouts: cache);
        var searched = cache.Searches;

        var baseline = cache.Baseline(problem, advice);
        var ranked = DiscardAdvisor.Rank(problem, baseline, cache);
        Assert.Equal(searched, cache.Searches);

        // 다음 계획. 빔은 계획을 넘어 살고 잣대만 버려진다.
        cache.BeginPlan();
        DiscardAdvisor.Rank(problem, cache.Baseline(problem, advice), cache);
        Assert.Equal(searched, cache.Searches);

        // 캐시를 안 준 쪽과 같은 답을 내야 한다. 안 그러면 위의 0은 답을 바꿔 번 것이다.
        Assert.Equal(
            ranked.Select(a => (a.InstanceId, a.Gain)),
            DiscardAdvisor.Rank(problem, baseline).Select(a => (a.InstanceId, a.Gain)));
    }

    /// <summary>
    /// 취소된 탐색은 캐시에 남지 않는다.
    ///
    /// 예전에는 취소 검사마다 <b>중간까지 푼 것을 돌려주었다.</b> 석판 몇 장만 놓은 빔과 첫 배치만
    /// 채점한 결과가 그대로 캐시에 들어갔고, 빌드마다 캐시를 새로 지었기 때문에만 안전했다.
    /// 캐시가 한 번의 계획보다 오래 살면 그 뒤의 모든 계획이 <b>석판을 한 장도 옮기지 말라</b>고
    /// 하게 된다. 입구는 셋이므로(<c>Of</c>·<c>Baseline</c>·<c>Yardstick</c>) 셋 다 본다.
    /// </summary>
    [Fact]
    public void ACancelledSearchLeavesNothingInTheCache()
    {
        var problem = FullBag(charmCount: 12, spare: 6);
        var stopped = SolverOptions.ForAdvice(new CancellationToken(true));
        var live = SolverOptions.ForAdvice(default);

        // 빔부터 취소된 경우. 열쇠에는 취소 신호가 없으므로 다음 빌드가 같은 칸을 들여다본다.
        var cold = new LayoutCache();
        Assert.Throws<OperationCanceledException>(() => cold.Of(problem, stopped));

        var layouts = cold.Of(problem, live);
        Assert.Equal(0, cold.Reuses);
        Assert.All(layouts, layout => Assert.Equal(problem.Tablets.Count, layout.Count));

        // 빔은 성한데 채점이 취소된 경우. 여기를 지나면 기준 배치와 잣대가 문다.
        var warm = new LayoutCache();
        warm.Of(problem, live);
        Assert.Throws<OperationCanceledException>(() => warm.Baseline(problem, stopped));
        Assert.Throws<OperationCanceledException>(() => warm.Yardstick(problem, stopped));

        var baseline = warm.Baseline(problem, live);
        Assert.Equal(problem.Tablets.Count, baseline.Tablets.Count);
        Assert.Equal(problem.Charms.Count, baseline.CharmPositions.Count);
        Assert.Equal(1, warm.Searches);
    }

    /// <summary>
    /// <see cref="FullBag"/>와 같은 모양을 스냅샷으로. 빔을 계획 사이에 돌려 쓰는 자리가
    /// <see cref="PlanBuilder"/>라 여기서는 판이 아니라 스냅샷이어야 한다. 검증이 통과해야
    /// 계획이 목표를 내놓고, 그래야 다음 계획이 직전 계획을 앵커로 받는다.
    /// </summary>
    private static (GameSnapshot Snapshot, Catalog Catalog) Board()
    {
        const int tablets = 3;
        const int charms = 11;
        var grid = new GridSpec(6, 7, tablets + charms);
        var queries = new[] { "RIGHT 1", "HORIZONTAL 2", "UP 1" };

        var tabletDefinitions = new List<TabletDefinition>
        {
            new() { Id = "mixed", EntityId = TabletMix.ResultEntityId },
            new() { Id = "ot", EntityId = 400, Query = "RIGHT 2" },
        };
        var charmDefinitions = new List<CharmDefinition>
        {
            new() { Id = "oc", EntityId = 300, MaxLevel = 5, Rarity = Rarity.Rare },
        };
        var inventory = new InventoryState { Width = grid.Width, Height = grid.Height, Storage = grid.Storage };

        for (var i = 0; i < tablets; i++)
        {
            tabletDefinitions.Add(new TabletDefinition { Id = "t" + i, EntityId = 700 + i, Query = queries[i] });
            inventory.Tablets.Add(new PlacedTablet
            {
                DefinitionId = 700 + i,
                InstanceId = 900 + i,
                Position = grid.ToPosition(i),
            });
        }
        for (var i = 0; i < charms; i++)
        {
            charmDefinitions.Add(new CharmDefinition { Id = "c" + i, EntityId = 200 + i, MaxLevel = 5 });
            inventory.Items.Add(new PlacedItem
            {
                DefinitionId = 200 + i,
                InstanceId = i,
                Position = grid.ToPosition(tablets + i),
            });
        }

        var snapshot = new GameSnapshot
        {
            Inventory = inventory,
            Run = new RunState { Gold = 1000 },
            Mixer = new MixerState { Cost = 0 },
        };
        snapshot.Offers.Add(new OfferedItem { DefinitionId = 300, Kind = "charm", SlotIndex = 0 });
        snapshot.Offers.Add(new OfferedItem { DefinitionId = 400, Kind = "tablet", SlotIndex = 1 });

        var catalog = new Catalog(tabletDefinitions, charmDefinitions);
        var probe = PlanBuilder.Build(snapshot, catalog, new PlanPreferences { Recommendations = false })!;
        inventory.LevelMatrix = probe.Current.CellLevels.ToDictionary(p => $"{p.Key.X},{p.Key.Y}", p => p.Value);
        inventory.DisabledCells = probe.Current.DisabledCells.Select(c => $"{c.X},{c.Y}").ToList();
        foreach (var item in inventory.Items)
        {
            item.EffectiveLevel = probe.Current.Levels.TryGetValue(item.Position, out var level) ? level : 0;
            item.IsActive = !probe.Current.DisabledCells.Contains(item.Position);
        }
        foreach (var tablet in inventory.Tablets)
            tablet.IsApplied = probe.Current.AppliedTablets.TryGetValue(tablet.InstanceId, out var applied) && applied;
        return (snapshot, catalog);
    }

    /// <summary>
    /// 같은 스냅샷을 다시 풀면 빔은 한 번도 다시 돌지 않고, 나오는 계획도 새로 탐색한 것과 같다.
    ///
    /// 캐시가 계획 하나보다 오래 살면서 생긴 약속이다. 돌려 쓰기가 듣는지는 탐색 횟수가, 답이
    /// 같은지는 재현 결과 대조가 말한다 - 둘 중 하나만 보면 "아무것도 안 하고 빨라졌다"를
    /// 놓친다.
    /// </summary>
    [Fact]
    public void ResolvingTheSameSnapshotSearchesNothingAndPlansTheSame()
    {
        var (snapshot, catalog) = Board();
        var preferences = new PlanPreferences();

        var warm = new LayoutCache();
        var first = PlanBuilder.Build(snapshot, catalog, preferences, out _, null, warm)!;
        Assert.True(first.Verification.Passed, first.Verification.Reason);
        Assert.NotEmpty(first.Targets);

        // 직전 계획이 처음 붙는 판까지가 입력이 달라지는 구간이다. 여기부터가 평소다.
        PlanBuilder.Build(snapshot, catalog, preferences, out _, first, warm);
        var searched = warm.Searches;
        Assert.True(searched > 0);

        var cached = PlanBuilder.Build(snapshot, catalog, preferences, out _, first, warm)!;
        var fresh = PlanBuilder.Build(snapshot, catalog, preferences, out _, first, new LayoutCache())!;

        Assert.Equal(searched, warm.Searches);
        Assert.Empty(ReplayResult.From(fresh).Differences(ReplayResult.From(cached)));
    }
}
