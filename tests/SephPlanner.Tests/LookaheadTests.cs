using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 되돌릴 수 없는 선택 - 후보 획득·석판 합성·버리기 - 이 다음에 열릴 칸을 보는지.
///
/// 배치는 폴링마다 다시 풀리고 <c>F8</c>이 옮겨 주므로 근시안이어도 되지만, 이 셋은 한 번 하면
/// 끝이다. 아래·오른쪽으로 뻗는 석판은 잠긴 칸 몫이 버려져 지금 가방에서만 값이 낮으므로,
/// 그것만 보고 줄을 세우면 두 층 뒤에 가장 좋았을 석판을 상점에서 흘려보낸다.
/// 판단의 경위는 docs/notes/LOOKAHEAD-2026-09-14.md 에 있다.
/// </summary>
public class LookaheadTests
{
    /// <summary>두 줄만 열린 판. 그래서 다음에 열릴 칸은 12번, 즉 (0,2)다.</summary>
    private static PlacementProblem TwoRowsOpen()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 2, 12) };
        for (var i = 0; i < 3; i++)
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = 10 + i,
                Definition = new CharmDefinition { Id = "c" + i, EntityId = 200 + i, MaxLevel = 5 },
                Worth = new CharmWorth { Base = 1, PerLevel = 1 },
            });
        }
        return problem;
    }

    private static OfferCandidate Tablet(string name, int entityId, string query) => new()
    {
        Kind = "tablet",
        Name = name,
        DefinitionId = entityId,
        Tablet = new TabletDefinition { Id = name, EntityId = entityId, Query = query },
    };

    /// <summary>
    /// 잠긴 칸으로 뻗는 석판이 지금 가방에서만 값이 낮은 상황. <c>IDX</c>는 절대 칸 번호라 어디에
    /// 놓아도 같은 칸을 가리키므로, 자리 고르기와 섞이지 않고 잠긴 칸만 떼어 볼 수 있다.
    /// </summary>
    [Fact]
    public void ATabletThatOnlyReachesLockedCellsRisesWhenTheNextCellIsCounted()
    {
        var problem = TwoRowsOpen();
        var near = Tablet("코앞", 400, "IDX 0 1");
        var far = Tablet("건너편", 401, "IDX 12 3");

        var now = OfferAdvisor.Rank(problem, new[] { near, far }, gold: 0);

        Assert.Equal("코앞", now[0].Candidate.Name);
        Assert.Equal(1, now[0].Gain, 3);
        Assert.Equal(0, now[1].Gain, 3);
        Assert.Null(now[0].SoonGain);

        var soon = OfferAdvisor.Rank(
            problem, new[] { near, far }, gold: 0, lookahead: new Lookahead(problem));

        Assert.Equal("건너편", soon[0].Candidate.Name);

        // 지금 가방의 증가분은 그대로 남는다. 화면이 둘을 같이 보여줘야 순위가 납득된다.
        Assert.Equal(0, soon[0].Gain, 3);
        Assert.Equal(3, soon[0].SoonGain!.Value, 3);
        Assert.Equal(1, soon[1].Gain, 3);
        Assert.Equal(1, soon[1].SoonGain!.Value, 3);
    }

    /// <summary>가방이 다 열렸으면 볼 앞이 없다. 그때는 지금 가방의 값이 그대로 답이다.</summary>
    [Fact]
    public void AFullyOpenBagHasNothingToLookAheadTo()
    {
        var problem = TwoRowsOpen();
        problem.Grid = GridSpec.WithStorage(GridSpec.DefaultWidth * GridSpec.DefaultHeight);
        var lookahead = new Lookahead(problem);

        Assert.False(lookahead.Available);
        Assert.Null(lookahead.Baseline(new LayoutCache(), SolverOptions.ForAdvice(default)));

        var advice = OfferAdvisor.Rank(
            problem, new[] { Tablet("코앞", 400, "IDX 0 1") }, gold: 0, lookahead: lookahead);

        Assert.Null(advice[0].SoonGain);
        Assert.Equal(advice[0].Gain, advice[0].RankedGain, 9);
    }

    /// <summary>
    /// 합성도 재료가 사라지므로 되돌릴 수 없다. 여기 판은 물건이 칸보다 하나 많아, 둘을 합쳐야
    /// 전부 들어간다 - 그래서 지금은 이득이지만 칸이 하나 열리면 그냥 들어가므로 이득이 사라진다.
    ///
    /// 앞보기 값이 <b>실제로 칸이 하나 더 열린 가방에서 잰 값과 같은지</b>까지 본다. 늘어난 판을
    /// 잘못 세우거나 기준을 지금 판 것으로 두면 숫자가 맞지 않는다.
    /// </summary>
    [Fact]
    public void MixingCountsTheNextCellToo()
    {
        var catalog = new Catalog(
            new[] { new TabletDefinition { Id = "mixed", EntityId = TabletMix.ResultEntityId } },
            Array.Empty<CharmDefinition>());

        var tight = OverfullBag(storage: 6);
        var advice = TabletMixAdvisor.Rank(
            tight, catalog, cost: 0, gold: 1000, lookahead: new Lookahead(tight));
        var mix = Assert.Single(advice);

        var grown = TabletMixAdvisor.Rank(OverfullBag(storage: 7), catalog, cost: 0, gold: 1000);

        Assert.NotNull(mix.SoonGain);
        Assert.Equal(Assert.Single(grown).Gain, mix.SoonGain!.Value, 6);
        Assert.True(mix.Gain > mix.SoonGain!.Value + 0.5,
            $"지금 {mix.Gain}, 다음 칸 {mix.SoonGain}. 빠듯해서 생긴 이득이 앞보기에서 걸러지지 않았다.");
        Assert.Equal(mix.SoonGain!.Value, mix.RankedGain, 9);
    }

    /// <summary>물건이 칸보다 하나 많은 판. 석판 둘을 합쳐야 전부 들어간다.</summary>
    private static PlacementProblem OverfullBag(int storage)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, storage) };
        for (var i = 0; i < 5; i++)
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = 10 + i,
                Definition = new CharmDefinition { Id = "c" + i, EntityId = 200 + i, MaxLevel = 5 },
                Worth = new CharmWorth { Base = 1, PerLevel = 1 },
            });
        }
        for (var i = 0; i < 2; i++)
        {
            problem.Tablets.Add(new TabletSlot
            {
                InstanceId = 900 + i,
                Rotatable = false,
                Definition = new TabletDefinition
                {
                    Id = "t" + i,
                    EntityId = 700 + i,
                    Query = $"IDX {i} 2",
                    IsRotatable = false,
                },
            });
        }
        return problem;
    }

    /// <summary>
    /// 버리기는 지금 판에서 이득이어도, 칸이 하나만 더 열리면 이득이 사라지는 것이 있다.
    /// 아이템은 돌아오지 않으므로 그런 것은 권하지 않는다.
    ///
    /// 판은 <see cref="DiscardAdvisorTests"/>의 것과 같다 - 자물쇠(양옆이 비어야 켜진다)가 가방이
    /// 빠듯해 꺼져 있고, 석판을 빼면 켜진다. 그런데 칸이 하나 열려도 똑같이 켜지므로, 석판을
    /// 버리는 것은 공짜로 얻을 것을 값을 치르고 사는 셈이다.
    /// </summary>
    [Fact]
    public void ADiscardThatOnlyPaysWhileTheBagIsTightIsNotRecommended()
    {
        var problem = TightBagWithALock();
        var baseline = PlacementSolver.Solve(problem);
        Assert.Contains(1, baseline.InactiveCharms);

        var withoutLookahead = DiscardAdvisor.Rank(problem, baseline);
        Assert.Equal(4, Assert.Single(withoutLookahead).InstanceId);

        var withLookahead = DiscardAdvisor.Rank(
            problem, baseline, layouts: null, lookahead: new Lookahead(problem));
        Assert.Empty(withLookahead);
    }

    /// <summary>자물쇠 하나와 석판 하나가 든 빠듯한 한 줄. 석판을 빼야 자물쇠가 켜진다.</summary>
    private static PlacementProblem TightBagWithALock()
    {
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(5, 1, 5),
            ComboCounts = new Dictionary<string, int> { ["TEST"] = 2 },
            Combos = _ => new ComboDefinition { Id = "TEST", Thresholds = { 2 } },
        };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition
            {
                EntityId = 1,
                Id = "자물쇠",
                CriteriaType = "CharmActivateCriteria_BothSidesAreEmpty",
                MaxLevel = 0,
            },
            Worth = new CharmWorth { Base = 20, PerLevel = 0 },
        });
        for (var id = 2; id <= 3; id++)
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Definition = new CharmDefinition { EntityId = id, Id = "일반", MaxLevel = 0, Categories = { "TEST" } },
            });
        foreach (var charm in problem.Charms) problem.CurrentCharms[charm.InstanceId] = new GridPos(charm.InstanceId - 1, 0);
        problem.Tablets.Add(new TabletSlot { InstanceId = 4, Definition = new TabletDefinition { EntityId = 4, Id = "석판" } });
        problem.CurrentTablets[4] = new TabletSpot(new GridPos(4, 0), 0);
        return problem;
    }
}
