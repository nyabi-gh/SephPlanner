using Newtonsoft.Json;
using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public sealed class EternalDirectionTests
{
    public static IEnumerable<object[]> Sides() =>
        from scales in Enum.GetValues<HorizontalSide>()
        from eternal in Enum.GetValues<HorizontalSide>()
        select new object[] { scales, eternal };

    [Theory]
    [MemberData(nameof(Sides))]
    public void IndependentDirectionsMatchExhaustiveDps(HorizontalSide scales, HorizontalSide eternal)
    {
        var problem = Problem();
        problem.ScalesSide = scales;
        problem.EternalSide = eternal;
        problem.Charms.Add(new()
        {
            InstanceId = 2,
            Definition = new()
            {
                Behavior = "Charm_FireIce",
                MaxLevel = 0,
                HorizontalStats = new() { LeftStat = "FIREDAMAGE", RightStat = "ICEDAMAGE", MainByLevel = new() { 2 }, OppositeByLevel = new() { 0 } },
                Combat = new() { Collected = true }
            }
        });
        problem.CurrentCharms[2] = new(0, 0);
        problem.Charms[0].Definition.Combat.Attacks.Add(new()
        { Action = CombatActionKind.Automatic, DamageKind = CombatDamageKind.FlameSword, IntervalByLevel = new() { 1 } });
        var current = PlacementSolver.Score(problem, [], problem.CurrentCharms);
        problem.Combat = CombatPlanning.Capture(problem, current, new()
        { ObservedStats = { ["FIREDAMAGE"] = 12, ["ICEDAMAGE"] = 100, ["FROSTRELICFLAME"] = 1 } },
            new() { WeaponSequence = new(), ScalesSide = scales, EternalSide = eternal });
        var optimum = double.NegativeInfinity;
        for (var a = 0; a < 6; a++)
            for (var b = 0; b < 6; b++)
            {
                if (a == b || !Fits(eternal, a) || !Fits(scales, b)) continue;
                var trial = PlacementSolver.Score(problem, [], new Dictionary<int, GridPos> { [1] = new(a, 0), [2] = new(b, 0) });
                optimum = Math.Max(optimum, trial.Score);
            }
        var best = PlacementSolver.Solve(problem);
        Assert.Empty(best.UnpositionedCharms);
        Assert.True(Fits(eternal, best.CharmPositions[1].X));
        Assert.True(Fits(scales, best.CharmPositions[2].X));
        Assert.Equal(optimum, best.Score);
        Assert.Equal(best.Score, best.Combat!.Dps);
        if (scales == HorizontalSide.Automatic && eternal == HorizontalSide.Automatic) Assert.Equal(102, best.Score);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("active")]
    [InlineData("held")]
    public void ConflictingDirectionsCannotProduceExecutablePlans(string conflict)
    {
        var (snapshot, catalog, preferences) = Inputs();
        preferences.Combat.EternalSide = HorizontalSide.Right;
        if (conflict == "locked") snapshot.Inventory!.Storage = 3;
        if (conflict == "active")
            for (var x = 3; x < 6; x++)
            {
                snapshot.Inventory!.FixedEffects.Add(new() { Position = new(x, 0), Level = -1 });
                snapshot.Inventory.LevelMatrix![x + ",0"] = -1;
            }
        if (conflict == "held")
        {
            snapshot.Inventory!.FixedEffects.Add(new() { Position = new(1, 0), IgnoreCriteria = 1 });
            preferences.HeldCharms.Add(1);
        }
        var plan = PlanBuilder.Build(snapshot, catalog, preferences)!;
        Assert.True(plan.Verification.Passed);
        Assert.Contains("영원의 식", Assert.Single(plan.PositionWarnings));
        Assert.Empty(plan.Targets);
        Assert.Empty(plan.Moves);
        Assert.False(plan.ManualMoveInstructionsAvailable);
        Assert.False(plan.HasUnapprovedDeactivation);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(6, true)]
    public void AcquisitionInheritsEternalDirection(int storage, bool available)
    {
        var problem = new PlacementProblem { Grid = new(6, 1, storage), EternalSide = HorizontalSide.Right };
        var offer = new OfferCandidate { DefinitionId = 1, Kind = "charm", Charm = Eternal() };
        Assert.Equal(available, Assert.Single(OfferAdvisor.Rank(problem, new[] { offer }, 0)).Available);
    }

    [Fact]
    public void DiscardAndMixCannotBypassEternalDirection()
    {
        var problem = Problem();
        problem.Grid = new(3, 1, 3);
        problem.Charms[0].Retained = true;
        problem.Charms.Add(new() { InstanceId = 2, Definition = new() { EntityId = 2 }, Worth = new() { Base = -5, PerLevel = 0 } });
        problem.CurrentCharms[2] = new(0, 0);
        Assert.Contains(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)), item => item.InstanceId == 2);
        problem.EternalSide = HorizontalSide.Right;
        Assert.Empty(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)));

        problem = Problem();
        problem.Grid = new(3, 1, 3);
        foreach (var (id, x) in new[] { (2, 0), (3, 2) })
        {
            problem.Tablets.Add(new() { InstanceId = id, Definition = new() { EntityId = id, Query = "RIGHT 1" } });
            problem.CurrentTablets[id] = new(new(x, 0), 0);
        }
        var catalog = new Catalog(problem.Tablets.Select(tablet => tablet.Definition), problem.Charms.Select(charm => charm.Definition));
        Assert.NotEmpty(TabletMixAdvisor.Rank(problem, catalog, 0, 0));
        problem.EternalSide = HorizontalSide.Right;
        Assert.Empty(TabletMixAdvisor.Rank(problem, catalog, 0, 0));
    }

    [Fact]
    public void CachedBaselineChangesWithEachIndependentDirection()
    {
        var problem = Problem();
        var cache = new LayoutCache();
        var options = new SolverOptions();
        var auto = cache.Baseline(problem, options);
        Assert.Same(auto, cache.Baseline(problem, options));
        problem.EternalSide = HorizontalSide.Right;
        var right = cache.Baseline(problem, options);
        Assert.NotSame(auto, right);
        Assert.True(right.CharmPositions[1].X >= 3);
        problem.EternalSide = HorizontalSide.Left;
        Assert.True(cache.Baseline(problem, options).CharmPositions[1].X < 3);
    }

    [Fact]
    public void MultipleEternalArtifactsReportCapacityWithoutTurningOffEffects()
    {
        var problem = Problem();
        problem.EternalSide = HorizontalSide.Right;
        for (var id = 2; id <= 4; id++)
        {
            problem.Charms.Add(new() { InstanceId = id, Definition = Eternal() });
            problem.CurrentCharms[id] = new(id, 0);
        }
        var result = PlacementSolver.Solve(problem);
        Assert.Single(result.UnpositionedCharms);
        Assert.Equal(4, result.CharmPositions.Count);
        Assert.Empty(result.InactiveCharms);
    }

    [Fact]
    public void NewReplayPreservesIndependentSettingsAndVerifiesTheirFingerprint()
    {
        var (snapshot, catalog, preferences) = Inputs();
        var original = PlanFingerprint.Full(snapshot, preferences, "eternal-catalog");
        preferences.Combat.EternalSide = HorizontalSide.Right;
        preferences.Combat.ScalesSide = HorizontalSide.Left;
        Assert.NotEqual(original, PlanFingerprint.Full(snapshot, preferences, "eternal-catalog"));
        using var runner = new PlanRunner(catalog);
        runner.Submit(snapshot, preferences, "eternal-catalog");
        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(10)), runner.State.Error);
        var replay = System.Text.Json.JsonSerializer.Deserialize<PlanReplay>(JsonConvert.SerializeObject(runner.CaptureReplay()))!;
        var rebuilt = replay.Rebuild();
        Assert.Contains("화염검 → 얼음", Assert.Single(rebuilt.PositionDetails));
        Assert.Empty(replay.Expected!.Differences(ReplayResult.From(rebuilt)));
        replay.Preferences!.Combat!.EternalSide = HorizontalSide.Left;
        Assert.Throws<InvalidDataException>(() => replay.Rebuild());
        Assert.Throws<ArgumentException>(() => CombatSimulator.Validate(new() { EternalSide = (HorizontalSide)99 }));
    }

    private static bool Fits(HorizontalSide side, int x) => side == HorizontalSide.Automatic || (x < 3) == (side == HorizontalSide.Left);
    private static CharmDefinition Eternal() => new()
    {
        EntityId = 1,
        Id = "Eternal",
        MaxLevel = 0,
        Behavior = "Charm_FireIceWeapon",
        Names = { ["current"] = "영원의 식" },
        Combat = new() { Collected = true, FireIcePosition = true }
    };
    private static PlacementProblem Problem() => new()
    { Grid = new(6, 1, 6), Charms = { new() { InstanceId = 1, Definition = Eternal() } }, CurrentCharms = { [1] = new(1, 0) } };
    private static (GameSnapshot, Catalog, PlanPreferences) Inputs() => (new()
    {
        Run = new() { Combat = new() { ObservedStats = { ["FROSTRELICFLAME"] = 1 } } },
        Inventory = new()
        {
            Width = 6,
            Height = 1,
            Storage = 6,
            LevelMatrix = new(),
            DisabledCells = new(),
            Items = { new() { InstanceId = 1, DefinitionId = 1, Position = new(1, 0), IsActive = true } }
        }
    }, new Catalog([], new[] { Eternal() }), new() { Recommendations = false });
}
