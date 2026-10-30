using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 게임 1.1.0 의 나침반. 1.0.33 침과 달리 부모 칸이 여럿이고, 꺼진 나침반은 사슬을 끊으며, 카테고리는
/// 이어진 뿌리 전부의 합집합이다(docs/RESEARCH.md 의 "침은 1.1.0 에서 나침반이 됐다").
/// </summary>
public class CompassTests
{
    private static readonly GridPos Up = new(0, -1);

    private static CharmSlot Root(int id, string category, Rarity rarity = Rarity.Common) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            EntityId = id,
            IsAttackable = true,
            MaxLevel = 4,
            Rarity = rarity,
            Categories = { category },
        },
    };

    private static CharmSlot DamageUp(int id, bool rarityCondition = false) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            EntityId = id,
            MaxLevel = 4,
            Behavior = "Charm_CompassDamageUp",
            DependencyParents = { Up },
            HasDependencyCondition = rarityCondition,
            DependencyMaxRarity = Rarity.Uncommon,
            DependencyBonusByLevel = { 10, 10, 10, 10, 10 },
            DependencyExtraByLevel = { 25, 25, 25, 25, 25 },
        },
    };

    private static CharmSlot Distribute(int id) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            EntityId = id,
            MaxLevel = 4,
            Behavior = "Charm_Compass",
            DependencyParents = { new(-1, 0), new(1, 0) },
        },
    };

    private static CharmSlot Breakthrough(int id) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            EntityId = id,
            MaxLevel = 4,
            Behavior = "Charm_CompassBreakthrough",
            DependencyParents = { Up },
        },
    };

    private static PlacementProblem Problem(GridSpec grid, params (CharmSlot Charm, GridPos Cell)[] placed)
    {
        var problem = new PlacementProblem { Grid = grid };
        foreach (var (charm, cell) in placed)
        {
            problem.Charms.Add(charm);
            problem.CurrentCharms[charm.InstanceId] = cell;
        }
        return problem;
    }

    private static Arrangement Score(PlacementProblem problem) =>
        PlacementSolver.Score(problem, new List<TabletPlacement>(), problem.CurrentCharms);

    private static Dictionary<GridPos, CharmSlot> ByCell(PlacementProblem problem) =>
        problem.Charms.ToDictionary(charm => problem.CurrentCharms[charm.InstanceId]);

    private static List<string> Categories(PlacementProblem problem, int id, bool withActivity)
    {
        var neighbors = ByCell(problem);
        var categories = new List<string>();
        var charm = problem.Charms.Single(slot => slot.InstanceId == id);
        if (!withActivity)
        {
            ComboCounting.PositionalCategories(charm, problem.CurrentCharms[id], neighbors, categories);
            return categories;
        }

        var occupancy = new GridOccupancy();
        foreach (var cell in problem.CurrentCharms.Values) occupancy.AddItem(cell, true);
        var result = TabletSimulator.Run(new List<TabletPlacement>(), occupancy, problem.Grid, problem.FixedEffects);
        ComboCounting.PositionalCategories(charm, problem.CurrentCharms[id], neighbors, categories,
            new CharmActivity(result, problem.Grid, occupancy));
        return categories;
    }

    private static PlacementProblem Fork() => Problem(new GridSpec(3, 2, 6),
        (Root(1, "EMBER"), new GridPos(0, 0)),
        (Distribute(2), new GridPos(1, 0)),
        (Root(3, "FROST"), new GridPos(2, 0)),
        (DamageUp(4), new GridPos(1, 1)));

    [Fact]
    public void ANeedleUnderTheDistributionCompassBoostsAndInheritsBothRoots()
    {
        var problem = Fork();
        var unit = problem.Charms[0].Worth.At(0);

        var scored = Score(problem);

        Assert.Empty(scored.UnlinkedCharms);
        // 뿌리 둘, 분배의 어림 몫 하나, 두 뿌리에 같은 몫을 주는 침이 둘.
        Assert.Equal(5 * unit, scored.Score, 6);
        Assert.Equal(new[] { "EMBER", "FROST" }, Categories(problem, 4, withActivity: false).OrderBy(c => c));
    }

    [Fact]
    public void AMissingRootLeavesTheOtherBranch()
    {
        var problem = Fork();
        problem.Charms[2].IsAttackable = false;
        var unit = problem.Charms[0].Worth.At(0);

        var scored = Score(problem);

        Assert.Empty(scored.UnlinkedCharms);
        Assert.Equal(4 * unit, scored.Score, 6);
        Assert.Equal(new[] { "EMBER" }, Categories(problem, 4, withActivity: false));
    }

    private static PlacementProblem Chain() => Problem(new GridSpec(1, 3, 3),
        (Root(1, "EMBER"), new GridPos(0, 0)),
        (DamageUp(2), new GridPos(0, 1)),
        (DamageUp(3), new GridPos(0, 2)));

    /// <summary>1.0.33 침은 꺼진 중간 침에서 끊기지 않았다(<see cref="NeedleConnectionTests"/>). 나침반은 끊긴다.</summary>
    [Fact]
    public void ADisabledMiddleCompassCutsTheChainAndItsOwnShare()
    {
        var problem = Chain();
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 1), Disable = 1 });

        var scored = Score(problem);

        Assert.Contains(2, scored.InactiveCharms);
        Assert.Contains(3, scored.UnlinkedCharms);
        Assert.Equal(problem.Charms[0].Worth.At(0), scored.Score, 6);
        Assert.Empty(Categories(problem, 3, withActivity: true));
        Assert.Empty(Categories(problem, 2, withActivity: true));
    }

    [Fact]
    public void AnActiveChainPassesTheRootUp()
    {
        var problem = Chain();
        var scored = Score(problem);

        Assert.Empty(scored.UnlinkedCharms);
        Assert.Equal(3 * problem.Charms[0].Worth.At(0), scored.Score, 6);
        Assert.Equal(new[] { "EMBER" }, Categories(problem, 3, withActivity: true));
    }

    [Fact]
    public void BreakthroughLiftsTheRarityConditionBelowIt()
    {
        var plain = Problem(new GridSpec(1, 3, 3),
            (Root(1, "EMBER", Rarity.Rare), new GridPos(0, 0)),
            (DamageUp(2), new GridPos(0, 1)),
            (DamageUp(3, rarityCondition: true), new GridPos(0, 2)));
        var lifted = Problem(new GridSpec(1, 3, 3),
            (Root(1, "EMBER", Rarity.Rare), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (DamageUp(3, rarityCondition: true), new GridPos(0, 2)));

        var neighbors = ByCell(lifted);
        var factor = Compass.Factor(lifted, lifted.Charms[2], new GridPos(0, 2), 0, neighbors, default);
        var withoutBreakthrough = Compass.Factor(plain, plain.Charms[2], new GridPos(0, 2), 0, ByCell(plain), default);

        Assert.Equal(1, withoutBreakthrough, 6);
        Assert.Equal(1 + 25.0 / 10, factor, 6);
    }

    [Fact]
    public void ADisabledCompassKeepsNoWorth()
    {
        var problem = Problem(new GridSpec(1, 2, 2),
            (Root(1, "EMBER"), new GridPos(0, 0)),
            (DamageUp(2), new GridPos(0, 1)));
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 1), Disable = 1 });

        Assert.Equal(problem.Charms[0].Worth.At(0), Score(problem).Score, 6);
    }

    /// <summary>
    /// 지금 자리 몫은 게임이 매긴 카테고리로 뺀다. 나침반이 꺼져 있어 게임이 아무것도 세지 않았는데
    /// 우리 셈으로 빼면 바탕 개수가 하나 모자라진다.
    /// </summary>
    [Fact]
    public void BaseCountsSubtractTheCategoriesTheGameReported()
    {
        var problem = Chain();
        problem.Charms[2].ObservedCategories = new List<string>();
        problem.Charms[1].ObservedCategories = new List<string> { "EMBER" };
        problem.ComboCounts = new Dictionary<string, int> { ["EMBER"] = 2 };

        Assert.Equal(1, ComboCounting.CountFor(problem, problem.Charms[2], "EMBER", null));
    }
}
