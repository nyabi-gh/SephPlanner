using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class CharmWorthCoverageTests
{
    private static CharmStatTable Table(int id, string status, params int[] values) => new()
    {
        EntityId = id,
        StatusId = status,
        ValuesByLevel = values.ToList(),
    };

    [Fact]
    public void AnUnconvertedConversionEffectSurvivesSerializationAndTheSolverKeepsItActive()
    {
        var charm = new CharmDefinition { EntityId = 1, MaxLevel = 4, Behavior = "Charm_StatusInstance" };
        CharmStatWorth.Apply(new[] { charm }, new StatMeasurement
        {
            CharmStats = { Table(1, "CONVERSION", 1), Table(1, "DAMAGE", -15, -10, -5, 0, 5) },
        });
        charm = JsonSerializer.Deserialize<CharmDefinition>(JsonSerializer.Serialize(charm))!;

        Assert.Equal(new[] { "CONVERSION" }, charm.StatWorthUnconverted);
        Assert.Equal(new[] { -3.0, -2, -1, 0, 1 }, charm.StatWorthByLevel);
        Assert.Equal(CharmWorthSource.MeasuredFloor, CharmWorth.Resolve(charm).Source);
        Assert.Contains(Explain.Charm(charm, null), line => line.Contains("일부만 측정"));

        var off = new GridPos(0, 0);
        var on = new GridPos(1, 0);
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(2, 1, 2),
            FixedEffects = { new FixedEffectCell { Position = off, Level = -1 } },
            Charms = { new CharmSlot { InstanceId = 1, Definition = charm } },
        };
        problem.CurrentCharms[1] = off;
        var plan = PlacementSolver.Solve(problem);
        Assert.Equal(on, plan.CharmPositions[1]);
        Assert.DoesNotContain(1, plan.InactiveCharms);
    }

    [Fact]
    public void CoverageIsIndependentOfSampleConfidenceAndRealPenaltiesRemain()
    {
        var charm = new CharmDefinition { EntityId = 1, MaxLevel = 1, Behavior = "Charm_StatusInstance" };
        CharmStatWorth.Apply(new[] { charm }, new StatMeasurement
        {
            CharmStats = { Table(1, "DAMAGE", -10, -5) },
        });
        Assert.Equal(0, charm.StatWorthConfidence);
        Assert.True(charm.StatWorthCoverageKnown);
        Assert.Empty(charm.StatWorthUnconverted);
        Assert.Equal(CharmWorthSource.Measured, CharmWorth.Resolve(charm).Source);
        Assert.Equal(-2, CharmWorth.Resolve(charm).At(0));
    }

    [Fact]
    public void MissingConversionIsDetectedAtLaterLevelsButNotForZeroOrUnreachableValues()
    {
        var measurement = new StatMeasurement
        {
            CharmStats =
            {
                Table(1, "LATER_PENALTY", 0, -1),
                Table(1, "ZERO", 0, 0),
                Table(2, "BEYOND_CAP", 0, -1),
            },
        };
        var report = CharmStatWorth.Run(measurement, id => id == 1 ? 1 : 0);
        Assert.Equal(new[] { "LATER_PENALTY" }, report.ByEntity[1].Unconverted);
        Assert.Empty(report.ByEntity[2].Unconverted);
    }

    [Fact]
    public void AMeasuredZeroLevelEffectDoesNotReceiveAnInventedActivationBonus()
    {
        var charm = new CharmDefinition { EntityId = 1, MaxLevel = 5, Behavior = "Charm_StatusInstance" };
        CharmStatWorth.Apply(new[] { charm }, new StatMeasurement
        {
            CharmStats = { Table(1, "BURN_STACK", 0, 1, 1, 2, 3, 4) },
        });
        var worth = CharmWorth.Resolve(charm);
        Assert.Equal(CharmWorthSource.Measured, worth.Source);
        Assert.Equal(0, worth.At(0));
        Assert.Equal(1, worth.At(1));
        var off = new GridPos(0, 0);
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(2, 1, 2),
            FixedEffects = { new FixedEffectCell { Position = off, Level = -1 } },
            Charms = { new CharmSlot { InstanceId = 1, Definition = charm } },
        };
        problem.CurrentCharms[1] = off;
        var preserved = PlacementSolver.Solve(problem);
        Assert.Equal(new GridPos(1, 0), preserved.CharmPositions[1]);
        Assert.Equal(0, preserved.Score);
        problem.Charms[0].AllowDeactivation = true;
        Assert.Equal(off, PlacementSolver.Solve(problem).CharmPositions[1]);
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(1, 0), Level = 1 });
        Assert.Equal(new GridPos(1, 0), PlacementSolver.Solve(problem).CharmPositions[1]);
    }

    [Fact]
    public void LegacyTablesDoNotClaimCompleteCoverageAndCuratedValuesStillWin()
    {
        var charm = JsonSerializer.Deserialize<CharmDefinition>(
            "{\"Behavior\":\"Charm_StatusInstance\",\"StatWorthByLevel\":[-3,-2]}")!;
        Assert.False(charm.StatWorthCoverageKnown);
        Assert.Equal(CharmWorthSource.MeasuredFloor, CharmWorth.Resolve(charm).Source);
        var curated = CharmWorth.Resolve(charm, new CharmValueEntry { Base = -4, PerLevel = 1 });
        Assert.Equal(CharmWorthSource.Curated, curated.Source);
        Assert.Equal(-4, curated.At(0));
    }

    [Fact]
    public void RemeasuringClearsStaleValuesAndCoverage()
    {
        var charm = new CharmDefinition { EntityId = 1, MaxLevel = 1, Behavior = "Charm_StatusInstance" };
        CharmStatWorth.Apply(new[] { charm }, new StatMeasurement { CharmStats = { Table(1, "FIXED", 1) } });
        Assert.NotEmpty(charm.StatWorthUnconverted);
        CharmStatWorth.Apply(new[] { charm }, new StatMeasurement { CharmStats = { Table(1, "DAMAGE", 2, 4) } });
        Assert.Empty(charm.StatWorthUnconverted);
        Assert.Equal(CharmWorthSource.Measured, CharmWorth.Resolve(charm).Source);
        CharmStatWorth.Apply(new[] { charm }, new StatMeasurement());
        Assert.Empty(charm.StatWorthByLevel);
        Assert.Equal(CharmWorthSource.Rarity, CharmWorth.Resolve(charm).Source);
    }
}
