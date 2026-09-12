using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class NeedleConnectionTests
{
    private static PlacementProblem Chain()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 3, 3) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition { EntityId = 1, IsAttackable = true, MaxLevel = 4, Categories = { "TEST" } },
        });
        for (var id = 2; id <= 3; id++)
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Retained = id == 3,
                Definition = new CharmDefinition
                {
                    EntityId = id,
                    MaxLevel = 4,
                    DependencyOffsetY = -1,
                    DependencyBonusByLevel = { 5, 10, 15, 20, 30 },
                },
            });
        for (var id = 1; id <= 3; id++) problem.CurrentCharms[id] = new GridPos(0, id - 1);
        return problem;
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("negative")]
    [InlineData("notAttackable")]
    [InlineData("missing")]
    public void RetainedNeedleRequiresAUsableFinalAttacker(string failure)
    {
        var problem = Chain();
        if (failure == "disabled") problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Disable = 1 });
        if (failure == "negative") problem.Charms[0].Enchant = -1;
        if (failure == "notAttackable") problem.Charms[0].IsAttackable = false;
        if (failure == "missing")
        {
            problem.Charms.RemoveAt(0);
            problem.CurrentCharms.Remove(1);
        }
        var scored = PlacementSolver.Score(problem, new List<TabletPlacement>(), problem.CurrentCharms);
        Assert.Contains(3, scored.UnlinkedCharms);
        Assert.Contains(3, scored.UnretainedCharms);
        Assert.Equal(failure == "notAttackable" ? problem.Charms[0].Worth.At(0) : 0, scored.Score);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InactiveIntermediateNeedleDoesNotBreakTheGameDependencyTraversal(bool negative)
    {
        var problem = Chain();
        if (negative) problem.Charms[1].Enchant = -1;
        else problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 1), Disable = 1 });
        var scored = PlacementSolver.Score(problem, new List<TabletPlacement>(), problem.CurrentCharms);
        Assert.Contains(2, scored.InactiveCharms);
        Assert.DoesNotContain(3, scored.UnlinkedCharms);
        Assert.Empty(scored.UnretainedCharms);
        Assert.Equal(problem.Charms.Sum(charm => charm.Worth.At(0)), scored.Score);
    }

    [Fact]
    public void InactiveTargetStillSuppliesInheritedCategories()
    {
        var problem = Chain();
        problem.Charms[0].Enchant = -1;
        var neighbors = problem.Charms.ToDictionary(charm => problem.CurrentCharms[charm.InstanceId]);
        var categories = new List<string>();
        ComboCounting.PositionalCategories(problem.Charms[2], new GridPos(0, 2), neighbors, categories);
        Assert.Contains("TEST", categories);
    }

    [Fact]
    public void RetainedNeedleProtectsItsFinalTargetFromDiscard()
    {
        var problem = Chain();
        problem.Charms[1].AllowDeactivation = true;
        problem.Charms[2].AllowDeactivation = true;
        problem.Charms[0].Definition.Categories.Clear();
        problem.Charms[0].Worth = new CharmWorth { Base = -100, PerLevel = 0 };
        var solved = PlacementSolver.Solve(problem);
        Assert.Empty(solved.UnretainedCharms);
        Assert.DoesNotContain(DiscardAdvisor.Rank(problem, solved), offer => offer.InstanceId == 1);
        problem.Charms[2].Retained = false;
        Assert.Contains(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)), offer => offer.InstanceId == 1);
    }

    [Fact]
    public void DependencyCycleDoesNotProduceDamageOrSatisfyRetention()
    {
        var problem = Chain();
        problem.Charms[1].Definition.DependencyOffsetY = 1;
        var scored = PlacementSolver.Score(problem, new List<TabletPlacement>(), problem.CurrentCharms);
        Assert.Contains(2, scored.UnlinkedCharms);
        Assert.Contains(3, scored.UnlinkedCharms);
        Assert.Contains(3, scored.UnretainedCharms);
        Assert.Equal(problem.Charms[0].Worth.At(0), scored.Score);
    }

    [Fact]
    public void AChainCanChangeDirectionAtEachNeedle()
    {
        var problem = Chain();
        problem.Grid = new GridSpec(2, 2, 4);
        problem.Charms[2].Definition.DependencyOffsetX = -1;
        problem.Charms[2].Definition.DependencyOffsetY = 0;
        problem.CurrentCharms[3] = new GridPos(1, 1);
        var scored = PlacementSolver.Score(problem, new List<TabletPlacement>(), problem.CurrentCharms);
        Assert.Empty(scored.UnlinkedCharms);
        Assert.Empty(scored.UnretainedCharms);
        Assert.Equal(problem.Charms.Sum(charm => charm.Worth.At(0)), scored.Score);
    }

    [Fact]
    public void ConnectedNeedleChainMovesTogetherToBetterCells()
    {
        var problem = Chain();
        problem.Grid = new GridSpec(2, 3, 6);
        for (var y = 0; y < 3; y++)
            problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(1, y), Level = 3 });
        var solved = PlacementSolver.Solve(problem);
        Assert.Empty(solved.UnlinkedCharms);
        Assert.Empty(solved.UnretainedCharms);
        Assert.All(solved.CharmPositions.Values, position => Assert.Equal(1, position.X));
        Assert.Equal(new GridPos(1, 0), solved.CharmPositions[1]);
        foreach (var pair in solved.CharmPositions) problem.CurrentCharms[pair.Key] = pair.Value;
        Assert.Equal(solved.CharmPositions.OrderBy(pair => pair.Key),
            PlacementSolver.Solve(problem).CharmPositions.OrderBy(pair => pair.Key));
    }

    /// <summary>
    /// 침의 레어도 덤은 <b>사용자가 낮춘 대상 위에서는 그만큼 줄어든다.</b>
    ///
    /// 제보 <c>fee2feb9</c>: 조건을 만족하는 대상 여섯 가운데 다섯이 낮춰진 것이었는데, 덤이
    /// 대상을 가리지 않아 침이 별 −2 짜리를 우선해서 강화했다. 값싼 것을 죽은 칸에 버려도 배수를
    /// 다 받았기 때문이다. 여기서는 낮춘 것과 원하는 것을 나란히 두고 침이 어느 쪽을 고르는지 본다.
    /// </summary>
    [Fact]
    public void TheNeedlePrefersATargetTheUserWants()
    {
        // 2x2. 아래 두 칸 중 하나에 침이 서고 바로 위 칸을 강화한다.
        var problem = new PlacementProblem { Grid = new GridSpec(2, 2, 4) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Weight = PlanPreferences.WeightOf(-2),
            Definition = new CharmDefinition { EntityId = 1, IsAttackable = true, MaxLevel = 4, Rarity = Rarity.Common },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Weight = PlanPreferences.WeightOf(1),
            Definition = new CharmDefinition { EntityId = 2, IsAttackable = true, MaxLevel = 4, Rarity = Rarity.Common },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 3,
            Definition = new CharmDefinition
            {
                EntityId = 3,
                MaxLevel = 4,
                DependencyOffsetY = -1,
                HasDependencyCondition = true,
                DependencyMaxRarity = Rarity.Common,
                DependencyBonusByLevel = { 10, 10, 10, 10, 10 },
                DependencyExtraByLevel = { 25, 25, 25, 25, 25 },
            },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 4,
            IsFiller = true,
            Definition = new CharmDefinition { EntityId = 4, MaxLevel = 1 },
        });

        var arrangement = PlacementSolver.Solve(problem);

        // 침 바로 위에 있는 것이 원하는 쪽이어야 한다.
        var needle = arrangement.CharmPositions[3];
        var above = needle.Offset(0, -1);
        Assert.Equal(above, arrangement.CharmPositions[2]);
        Assert.NotEqual(above, arrangement.CharmPositions[1]);
    }
}
