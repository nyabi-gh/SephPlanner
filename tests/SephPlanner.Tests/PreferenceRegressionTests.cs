using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class PreferenceRegressionTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public void CrystalPreferenceIncludesItsNeighborEffect(int neighborLevel)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 3) };
        var crystal = new CharmSlot
        {
            InstanceId = 1,
            Weight = 1,
            Definition = new CharmDefinition { MaxLevel = 1, Behavior = "Charm_NearLevelDamage", NeighborLevelBonus = { 1, 2 } },
            Worth = new CharmWorth { Base = 0, PerLevel = 0 }
        };
        problem.Charms.Add(crystal);
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Enchant = neighborLevel, Definition = new CharmDefinition { MaxLevel = 3 } });
        var positions = new Dictionary<int, GridPos> { [1] = new(0, 0), [2] = new(1, 0) };
        var first = PlacementSolver.Score(problem, new List<TabletPlacement>(), positions).Score;
        crystal.Weight = 10;
        var second = PlacementSolver.Score(problem, new List<TabletPlacement>(), positions).Score;
        Assert.Equal(9 * Math.Max(0, neighborLevel) * Worth.DamageBonus, second - first, 8);
    }

    [Theory]
    [InlineData(0.1, -5.6)]
    [InlineData(1, -2)]
    [InlineData(10, 34)]
    public void PreferenceScalesBenefitsWithoutMultiplyingPenalties(double weight, double expected)
    {
        var definition = new CharmDefinition
        {
            Behavior = "Charm_StatusInstance",
            StatWorthCoverageKnown = true,
            StatWorthByLevel = { -2 },
            StatBenefitByLevel = { 4 },
            StatPenaltyByLevel = { -6 }
        };
        Assert.Equal(expected, CharmWorth.Resolve(definition).WeightedAt(0, weight), 8);
    }

    [Fact]
    public void MeasurementKeepsOpposingStatsBeforeTheyCancel()
    {
        var definition = new CharmDefinition { EntityId = 1, MaxLevel = 1, Behavior = "Charm_StatusInstance" };
        var measurement = new StatMeasurement
        {
            CharmStats = {
            new CharmStatTable { EntityId = 1, StatusId = "DEFENSE", ValuesByLevel = { 5, 10 } },
            new CharmStatTable { EntityId = 2, StatusId = "HP", ValuesByLevel = { 0, 10 } },
            new CharmStatTable { EntityId = 1, StatusId = "HP", ValuesByLevel = { -10, -20 } } }
        };
        CharmStatWorth.Apply(new[] { definition }, measurement);
        Assert.All(definition.StatBenefitByLevel, value => Assert.True(value > 0));
        Assert.All(definition.StatPenaltyByLevel, value => Assert.True(value < 0));
        for (var level = 0; level <= 1; level++)
        {
            var worth = CharmWorth.Resolve(definition);
            Assert.Equal(worth.At(level), worth.WeightedAt(level, 1), 8);
            Assert.True(worth.WeightedAt(level, 10) > worth.WeightedAt(level, 1));
        }
    }

    [Fact]
    public void EstimatedFloorPreservesMeasuredPenaltyAndNeutralValue()
    {
        var worth = new CharmWorth
        {
            Base = 2,
            PerLevel = 0,
            ByLevelIsFloor = true,
            ByLevel = new[] { -2d },
            BenefitByLevel = new[] { 4d },
            PenaltyByLevel = new[] { -6d }
        };
        Assert.Equal(2, worth.WeightedAt(0, 1));
        Assert.Equal(74, worth.WeightedAt(0, 10));
    }

    private static PlacementProblem RetentionBoard(int storage = 2)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 1, storage) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Retained = true,
            Definition = new CharmDefinition { EntityId = 1, MaxLevel = 0 },
            Worth = new CharmWorth { Base = -10, PerLevel = 0 }
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Definition = new CharmDefinition { EntityId = 2, MaxLevel = 0 },
            Worth = new CharmWorth { Base = 1e8, PerLevel = 0 }
        });
        if (storage > 1) problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(1, 0), Level = -1 });
        return problem;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RetentionTakesPrecedenceOverScoreEvenWhenCapacityIsShort(int storage)
    {
        var problem = RetentionBoard(storage);
        var solved = PlacementSolver.Solve(problem);
        Assert.Empty(solved.UnretainedCharms);
        Assert.DoesNotContain(1, solved.InactiveCharms);
        Assert.Equal(new GridPos(0, 0), solved.CharmPositions[1]);
    }

    [Fact]
    public void DormantRetainedCharmReportsFailure()
    {
        var problem = RetentionBoard();
        problem.Charms[0].IsDormant = true;
        Assert.Equal(new[] { 1 }, PlacementSolver.Solve(problem).UnretainedCharms);
    }

    [Fact]
    public void RetentionFindsAnEmptySidesLayoutDespiteNegativeWorth()
    {
        var problem = RetentionBoard(6);
        problem.Charms[0].Definition.CriteriaType = "BothSidesAreEmpty";
        for (var id = 3; id <= 4; id++) problem.Charms.Add(new CharmSlot { InstanceId = id });
        var solved = PlacementSolver.Solve(problem);
        Assert.Empty(solved.UnretainedCharms);
        Assert.DoesNotContain(1, solved.InactiveCharms);
    }

    [Fact]
    public void OffersCannotReplaceRetainedItemsOrDisableThemIndirectly()
    {
        var problem = RetentionBoard(1);
        problem.Charms.RemoveAt(1);
        var offer = new OfferCandidate { Charm = new CharmDefinition { MaxLevel = 10 } };
        Assert.False(OfferAdvisor.Rank(problem, new[] { offer }, 100)[0].Available);
        problem.Grid = new GridSpec(6, 1, 2);
        var tablet = new OfferCandidate { Tablet = new TabletDefinition { Query = "HORIZONTAL -10" } };
        Assert.False(OfferAdvisor.Rank(problem, new[] { tablet }, 100)[0].Available);
    }

    [Fact]
    public void CrystalSmallBoardMatchesExhaustiveAssignmentsAndRemainsStable()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 1, 6) };
        for (var id = 1; id <= 6; id++) problem.Charms.Add(new CharmSlot
        {
            InstanceId = id,
            Enchant = id % 4,
            Definition = new CharmDefinition { MaxLevel = 3 }
        });
        problem.Charms[0].Definition.Behavior = "Charm_NearLevelDamage";
        problem.Charms[0].Definition.NeighborLevelBonus.AddRange(new[] { 1d, 2d });
        problem.Charms[0].Weight = 10;
        var positions = new Dictionary<int, GridPos>();
        var best = double.NegativeInfinity;
        void Enumerate(int id)
        {
            if (id == 7) { best = Math.Max(best, PlacementSolver.Score(problem, new List<TabletPlacement>(), positions).Score); return; }
            for (var x = 0; x < 6; x++)
            {
                var cell = new GridPos(x, 0);
                if (positions.ContainsValue(cell)) continue;
                positions[id] = cell;
                Enumerate(id + 1);
                positions.Remove(id);
            }
        }
        Enumerate(1);
        var solved = PlacementSolver.Solve(problem);
        Assert.Equal(best, solved.Score, 8);
        foreach (var pair in solved.CharmPositions) problem.CurrentCharms[pair.Key] = pair.Value;
        var repeated = PlacementSolver.Solve(problem);
        Assert.Equal(solved.CharmPositions.OrderBy(p => p.Key), repeated.CharmPositions.OrderBy(p => p.Key));
    }

    [Fact]
    public void FailedRetentionClearsManualAndAutomaticTargets()
    {
        var definition = new CharmDefinition { EntityId = 1, MaxLevel = 3 };
        var catalog = new Catalog(Array.Empty<TabletDefinition>(), new[] { definition });
        var snapshot = new GameSnapshot { Inventory = new InventoryState { Width = 6, Height = 1, Storage = 1 } };
        snapshot.Inventory.Items.Add(new PlacedItem { DefinitionId = 1, InstanceId = 10, Position = new GridPos(0, 0), Enchant = -1 });
        var preferences = new PlanPreferences { Recommendations = false, RetainedCharms = { 1 } };
        var plan = PlanBuilder.Build(snapshot, catalog, preferences)!;
        Assert.Equal(new[] { 10 }, plan.Best.UnretainedCharms);
        Assert.Empty(plan.Targets);
        Assert.Empty(plan.Moves);
        Assert.Contains("레벨이 0 미만", Assert.Single(plan.RetentionWarnings));
    }
}
