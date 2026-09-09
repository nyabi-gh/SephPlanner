using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class ContextStatTests
{
    private static CharmSlot Charm(StatCountSource source) => new()
    {
        InstanceId = 1,
        Definition = new CharmDefinition
        {
            MaxLevel = 1,
            StatWorthCoverageKnown = true,
            ContextStats = { new ContextStatBonus { Source = source, SlotCount = 6, StatusId = "TEST", WorthPerUnit = 1, AmountByLevel = { 2, 3 } } },
        },
    };

    private static double Score(PlacementProblem problem) =>
        PlacementSolver.Score(problem, new List<TabletPlacement>(), problem.CurrentCharms).Score;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(3, 6)]
    public void ScaleCountsTabletsButNotEngravings(int count, double expected)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 2, 12) };
        problem.Charms.Add(Charm(StatCountSource.StoneTablets));
        problem.CurrentCharms[1] = new GridPos(0, 0);
        for (var i = 0; i < count; i++) problem.Tablets.Add(new TabletSlot { InstanceId = i + 10 });
        problem.FixedTablets.Add(new TabletPlacement());
        Assert.Equal(expected, Score(problem));
    }

    [Fact]
    public void BeltCountsItselfAndInactiveCharmsButNotFillersOrLaterSlots()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(4, 2, 8) };
        problem.Charms.Add(Charm(StatCountSource.QuickSlotCharms));
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.Charms.Add(new CharmSlot { InstanceId = 2, IsDormant = true });
        problem.CurrentCharms[2] = new GridPos(1, 1);
        problem.Charms.Add(new CharmSlot { InstanceId = 3, IsFiller = true });
        problem.CurrentCharms[3] = new GridPos(2, 0);
        problem.Charms.Add(new CharmSlot { InstanceId = 4, IsDormant = true });
        problem.CurrentCharms[4] = new GridPos(2, 1);
        Assert.Equal(4, Score(problem));
        problem.CurrentCharms[2] = new GridPos(3, 1);
        Assert.Equal(2, Score(problem));
    }

    [Fact]
    public void DisabledBeltHasNoQuantityBonus()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 1, 1) };
        problem.Charms.Add(Charm(StatCountSource.QuickSlotCharms));
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Disable = 1 });
        Assert.Equal(0, Score(problem));
    }

    [Fact]
    public void SolverMovesCharmsIntoQuickSlotsInsteadOfLeavingConsumablesThere()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(4, 2, 8) };
        problem.Charms.Add(Charm(StatCountSource.QuickSlotCharms));
        problem.CurrentCharms[1] = new GridPos(2, 1);
        for (var id = 2; id <= 8; id++)
        {
            problem.Charms.Add(new CharmSlot { InstanceId = id, IsFiller = id != 8, IsDormant = id == 8 });
            problem.CurrentCharms[id] = problem.Grid.ToPosition(id == 8 ? 7 : id - 2);
        }
        var solved = PlacementSolver.Solve(problem);
        Assert.Equal(4, solved.Score);
        foreach (var id in new[] { 1, 8 })
            Assert.True(problem.Grid.ToIndex(solved.CharmPositions[id].X, solved.CharmPositions[id].Y) < 6);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    public void KeyUsesTheCategoryOfItsRow(int row, double expected)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 3, 3) };
        var key = Charm(StatCountSource.RowCategory);
        key.Definition.LineCategories.AddRange(new[] { "EMBER", "OTHER" });
        key.Definition.ContextStats[0].Category = "EMBER";
        problem.Charms.Add(key);
        problem.CurrentCharms[1] = new GridPos(0, row);
        Assert.Equal(expected, Score(problem));
    }

    [Fact]
    public void StaticAndContextStatsCombineWithoutRarityOrPenaltyDuplication()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 1, 1) };
        var belt = Charm(StatCountSource.QuickSlotCharms);
        belt.Weight = 4;
        belt.Enchant = 10;
        belt.Definition.StatBenefitByLevel.AddRange(new double[] { 1, 2 });
        belt.Definition.StatPenaltyByLevel.AddRange(new double[] { -5, -6 });
        problem.Charms.Add(belt);
        problem.CurrentCharms[1] = new GridPos(0, 0);
        Assert.Equal(14, Score(problem), 2);
    }

    [Fact]
    public void ConversionRatesComeFromIndependentStaticSamplesAndRemainMissingWhenUnknown()
    {
        var charm = Charm(StatCountSource.QuickSlotCharms).Definition;
        var measurement = new StatMeasurement
        {
            CharmStats = { new CharmStatTable { EntityId = 2, StatusId = "TEST", ValuesByLevel = { 0, 4 } } },
        };
        CharmStatWorth.Apply(new[] { charm }, measurement);
        Assert.Equal(0.25, charm.ContextStats[0].WorthPerUnit);
        Assert.Empty(charm.StatWorthByLevel);
        CharmStatWorth.Apply(new[] { charm }, new StatMeasurement());
        Assert.Null(charm.ContextStats[0].WorthPerUnit);
    }
}
