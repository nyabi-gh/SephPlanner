using System.Text.Json;
using Newtonsoft.Json;
using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public sealed class ItemFeedbackTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConfirmedEmptyEffectUsesNegativeCellAndFreesUsefulSpace(bool dps, bool competitor)
    {
        var problem = new PlacementProblem { Grid = new(2, 1, 2) };
        problem.FixedEffects.Add(new() { Position = new(1, 0), Level = -1 });
        problem.Charms.Add(Burden());
        problem.CurrentCharms[1] = new(0, 0);
        if (competitor)
        {
            var other = new CharmSlot { InstanceId = 2, Definition = new() { EntityId = 2, MaxLevel = 0, Combat = new() { Collected = true } } };
            other.Definition.Combat.Stats.Add(new() { Key = "PHYSICALDAMAGE", Values = new() { 100 } });
            problem.Charms.Add(other);
            problem.CurrentCharms[2] = new(1, 0);
        }
        if (dps) Capture(problem, "PHYSICALDAMAGE");
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(new GridPos(1, 0), result.CharmPositions[1]);
        Assert.Empty(result.UnpreservedCharms);
        Assert.Empty(result.UnapprovedDeactivations);
        Assert.Equal(0, result.UnsafeEmptyCells);
        Assert.False(problem.Charms[0].IsFiller);
        if (competitor) Assert.DoesNotContain(2, result.InactiveCharms);
    }

    [Theory]
    [InlineData("retained")]
    [InlineData("held")]
    [InlineData("unknown")]
    [InlineData("defensive")]
    public void ExplicitPoliciesAndUnprovenOrDefensiveEffectsStayProtected(string kind)
    {
        var charm = Burden();
        charm.Retained = kind == "retained";
        charm.Held = kind == "held";
        if (kind is "unknown" or "defensive") charm.Definition.HasNoActivationEffect = false;
        if (kind == "defensive") charm.Definition.Combat.Stats.Add(new() { Key = "DAMAGEREDUCTION", Values = new() { 10 } });
        var problem = new PlacementProblem { Grid = new(2, 1, 2), Charms = { charm } };
        problem.CurrentCharms[1] = new(0, 0);
        problem.FixedEffects.Add(new() { Position = new(1, 0), Level = -1 });
        problem.FixedEffects.Add(new() { Position = new(0, 0), IgnoreCriteria = 1 });
        Capture(problem, "PHYSICALDAMAGE");
        Assert.Equal(new GridPos(0, 0), PlacementSolver.Solve(problem).CharmPositions[1]);
    }

    [Fact]
    public void EmptyEffectStillCountsAsAnArtifactAndNegativeNeighbor()
    {
        var burden = Burden();
        var crystal = new CharmSlot
        {
            InstanceId = 2,
            Definition = new()
            { MaxLevel = 0, NeighborLevelBonus = new() { 2 }, Combat = new() { Collected = true } }
        };
        var count = new CharmSlot
        {
            InstanceId = 3,
            Definition = new()
            {
                ContextStats = { new() { Source = StatCountSource.QuickSlotCharms, SlotCount = 6, CombatKey = "FIREDAMAGE", AmountByLevel = new() { 3 } } },
                Combat = new() { Collected = true }
            }
        };
        var problem = new PlacementProblem { Grid = new(6, 1, 6) };
        var context = new PlacementCombatContext();
        var placed = new List<LocatedCombatCharm>
        {
            new() { Charm = burden, Position = new(1, 0), Level = -1, Active = false },
            new() { Charm = crystal, Position = new(0, 0), Active = true },
            new() { Charm = count, Position = new(5, 0), Active = true },
        };
        var nearby = CombatLoadouts.Build(problem, context, placed);
        Assert.Equal(-2, nearby.Stats.Read("ALLDAMAGEBONUS"));
        Assert.Equal(9, nearby.Stats.Read("FIREDAMAGE"));
        placed[0].Position = new(3, 0);
        Assert.Equal(0, CombatLoadouts.Build(problem, context, placed).Stats.Read("ALLDAMAGEBONUS"));
    }

    [Theory]
    [InlineData(2, 0, true, 2, 0)]
    [InlineData(3, 0, true, 0, 2)]
    [InlineData(2, 1, true, 7, 1)]
    [InlineData(3, 1, true, 1, 7)]
    [InlineData(0, 99, true, 11, 4)]
    [InlineData(5, 2, true, 4, 11)]
    [InlineData(2, -1, false, 0, 0)]
    [InlineData(3, 2, false, 0, 0)]
    public void ScalesUseCapturedTablesAtTheFixedColumnBoundary(int x, int level, bool active, int fire, int ice)
    {
        var charm = Scales();
        var placed = new[] { new LocatedCombatCharm { Charm = charm, Position = new(x, 4), Level = level, Active = active } };
        var loadout = CombatLoadouts.Build(new(), new(), placed);
        Assert.Equal(fire, loadout.Stats.Read("FIREDAMAGE"));
        Assert.Equal(ice, loadout.Stats.Read("ICEDAMAGE"));
    }

    [Fact]
    public void ObservedContributionIsRemovedBeforeMovingOrDisablingScales()
    {
        var problem = ScalesProblem();
        Capture(problem, "FIREDAMAGE");
        var context = problem.Combat!;
        var item = new LocatedCombatCharm { Charm = problem.Charms[0], Position = new(3, 0), Active = true };
        Assert.Equal(98, context.Background.Read("FIREDAMAGE"));
        Assert.Equal(98, CombatLoadouts.Build(problem, context, new[] { item }).Stats.Read("FIREDAMAGE"));
        item.Level = 1;
        Assert.Equal(99, CombatLoadouts.Build(problem, context, new[] { item }).Stats.Read("FIREDAMAGE"));
        item.Active = false;
        Assert.Equal(98, CombatLoadouts.Build(problem, context, new[] { item }).Stats.Read("FIREDAMAGE"));
        item.Active = true;
        item.Position = new(2, 0);
        Assert.Equal(105, CombatLoadouts.Build(problem, context, new[] { item }).Stats.Read("FIREDAMAGE"));
    }

    [Theory]
    [InlineData("FIREDAMAGE", HorizontalSide.Automatic, true, 100)]
    [InlineData("ICEDAMAGE", HorizontalSide.Automatic, false, 102)]
    [InlineData("FIREDAMAGE", HorizontalSide.Right, false, 98)]
    [InlineData("ICEDAMAGE", HorizontalSide.Left, true, 100)]
    public void AutoChoosesDpsAndExplicitSideRemainsAConstraint(string stat, HorizontalSide side, bool left, double dps)
    {
        var problem = ScalesProblem();
        problem.ScalesSide = side;
        Capture(problem, stat);
        var current = PlacementSolver.Score(problem, [], problem.CurrentCharms);
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(left, HorizontalStatBonus.IsLeft(result.CharmPositions[1]));
        Assert.Equal(dps, result.Score);
        Assert.Empty(result.UnpositionedCharms);
        Assert.Empty(CombatChangeAssessment.Compare(problem, current, result));
        problem.CurrentCharms[1] = result.CharmPositions[1];
        Assert.Equal(result.CharmPositions[1], PlacementSolver.Solve(problem).CharmPositions[1]);
    }

    [Fact]
    public void ScalesAndEternalConversionAreSolvedTogetherAgainstEnumeration()
    {
        var problem = ScalesProblem();
        var eternal = new CharmSlot { InstanceId = 2, Definition = new() { MaxLevel = 0, Combat = new() { Collected = true, FireIcePosition = true } } };
        eternal.Definition.Combat.Attacks.Add(new()
        { Action = CombatActionKind.Automatic, DamageKind = CombatDamageKind.FlameSword, IntervalByLevel = new() { 1 } });
        problem.Charms.Add(eternal);
        problem.CurrentCharms[2] = new(1, 0);
        var current = PlacementSolver.Score(problem, [], problem.CurrentCharms);
        problem.Combat = CombatPlanning.Capture(problem, current, new()
        { ObservedStats = { ["FIREDAMAGE"] = 12, ["ICEDAMAGE"] = 100, ["FROSTRELICFLAME"] = 1 } }, new() { WeaponSequence = new() });
        var all = new List<Arrangement>();
        for (var a = 0; a < 6; a++)
            for (var b = 0; b < 6; b++)
                if (a != b) all.Add(PlacementSolver.Score(problem, [], new Dictionary<int, GridPos> { [1] = new(a, 0), [2] = new(b, 0) }));
        var best = PlacementSolver.Solve(problem);
        Assert.Equal(all.Max(candidate => candidate.Score), best.Score);
        Assert.Equal(102, best.Score);
        Assert.False(HorizontalStatBonus.IsLeft(best.CharmPositions[1]));
        Assert.False(HorizontalStatBonus.IsLeft(best.CharmPositions[2]));
    }

    [Fact]
    public void ScalesKeepBothCategoriesAndUnmodeledHorizontalStatsAreReported()
    {
        var problem = ScalesProblem();
        var charm = problem.Charms[0];
        charm.Definition.Categories.AddRange(new[] { "EMBER", "GLACIER" });
        var left = ComboCounting.CountAll(new Dictionary<GridPos, CharmSlot> { [new(2, 0)] = charm });
        var right = ComboCounting.CountAll(new Dictionary<GridPos, CharmSlot> { [new(3, 0)] = charm });
        Assert.Equal(left.OrderBy(pair => pair.Key), right.OrderBy(pair => pair.Key));
        Assert.Equal(1, left["EMBER"]);
        Assert.Equal(1, left["GLACIER"]);
        charm.Definition.Categories.Clear();
        charm.Definition.HorizontalStats!.LeftStat = "UNKNOWN";
        Capture(problem, "FIREDAMAGE");
        var current = PlacementSolver.Score(problem, [], problem.CurrentCharms);
        var changed = PlacementSolver.Score(problem, [], new Dictionary<int, GridPos> { [1] = new(3, 0) });
        Assert.NotEmpty(CombatChangeAssessment.Compare(problem, current, changed));
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("active")]
    [InlineData("held")]
    public void UnavailableSideProducesWarningsAndNoExecutablePlan(string conflict)
    {
        var (snapshot, catalog, preferences) = PlanInputs();
        preferences.Combat.ScalesSide = HorizontalSide.Right;
        if (conflict == "locked") snapshot.Inventory!.Storage = 3;
        if (conflict == "active")
            for (var x = 3; x < 6; x++)
            {
                snapshot.Inventory!.FixedEffects.Add(new() { Position = new(x, 0), Level = -1 });
                snapshot.Inventory.LevelMatrix![x + ",0"] = -1;
            }
        if (conflict == "held")
        {
            snapshot.Inventory!.FixedEffects.Add(new() { Position = new(0, 0), IgnoreCriteria = 1 });
            preferences.HeldCharms.Add(1144);
        }
        var plan = PlanBuilder.Build(snapshot, catalog, preferences)!;
        Assert.True(plan.Verification.Passed);
        Assert.NotEmpty(plan.Best.UnpositionedCharms);
        Assert.Contains("천칭", Assert.Single(plan.PositionWarnings));
        Assert.Empty(plan.Targets);
        Assert.Empty(plan.Moves);
        Assert.False(plan.ManualMoveInstructionsAvailable);
        Assert.False(plan.HasUnapprovedDeactivation);
    }

    [Fact]
    public void DirectionAndEffectMetadataSurvivePluginJsonAndChangeRequestFingerprint()
    {
        var (snapshot, catalog, preferences) = PlanInputs();
        var initial = PlanFingerprint.Full(snapshot, preferences, "catalog");
        preferences.Combat.ScalesSide = HorizontalSide.Right;
        Assert.NotEqual(initial, PlanFingerprint.Full(snapshot, preferences, "catalog"));
        var serialized = JsonConvert.SerializeObject(new { Preferences = ReplayPreferences.From(preferences), Charm = catalog.Charm(1144), Burden = Burden().Definition });
        using var json = JsonDocument.Parse(serialized);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ReplayPreferences>(json.RootElement.GetProperty("Preferences").GetRawText())!.Restore();
        var definition = System.Text.Json.JsonSerializer.Deserialize<CharmDefinition>(json.RootElement.GetProperty("Charm").GetRawText())!;
        var burden = System.Text.Json.JsonSerializer.Deserialize<CharmDefinition>(json.RootElement.GetProperty("Burden").GetRawText())!;
        Assert.True(burden.HasNoActivationEffect);
        Assert.Equal(new[] { 2, 7, 11 }, definition.HorizontalStats!.MainByLevel);
        Assert.Equal(new[] { 0, 1, 4 }, definition.HorizontalStats.OppositeByLevel);
        Assert.Equal(HorizontalSide.Right, restored.Combat.ScalesSide);
        var plan = PlanBuilder.Build(snapshot, new Catalog([], new[] { definition }), restored)!;
        Assert.Empty(plan.PositionWarnings);
        Assert.Contains("오른쪽", Assert.Single(plan.PositionDetails));
        Assert.NotEmpty(plan.Targets);
        Assert.Equal(98, plan.Best.Score);
    }

    [Fact]
    public void InvalidSideIsRejectedInsteadOfBecomingAnImplicitPreference()
    {
        Assert.Throws<ArgumentException>(() => CombatSimulator.Validate(new() { ScalesSide = (HorizontalSide)99 }));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(6, true)]
    public void NewScalesOffersInheritTheSelectedSide(int storage, bool available)
    {
        var problem = new PlacementProblem { Grid = new(6, 1, storage), ScalesSide = HorizontalSide.Right };
        var candidate = new OfferCandidate { DefinitionId = 1144, Kind = "charm", Charm = Scales().Definition };
        Assert.Equal(available, Assert.Single(OfferAdvisor.Rank(problem, new[] { candidate }, 0)).Available);
    }

    [Fact]
    public void DiscardAdviceCannotBypassTheSelectedSide()
    {
        var problem = ScalesProblem();
        problem.Grid = new(3, 1, 3);
        problem.Charms[0].Retained = true;
        problem.Charms.Add(new() { InstanceId = 2, Definition = new() { EntityId = 2 }, Worth = new() { Base = -5, PerLevel = 0 } });
        problem.CurrentCharms[2] = new(1, 0);
        Assert.Contains(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)), item => item.InstanceId == 2);
        problem.ScalesSide = HorizontalSide.Right;
        Assert.Empty(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)));
    }

    [Fact]
    public void MixAdviceCannotBypassTheSelectedSide()
    {
        var problem = ScalesProblem();
        problem.Grid = new(3, 1, 3);
        for (var id = 2; id <= 3; id++)
        {
            problem.Tablets.Add(new() { InstanceId = id, Definition = new() { EntityId = id, Query = "RIGHT 1" } });
            problem.CurrentTablets[id] = new(new(id - 1, 0), 0);
        }
        var catalog = new Catalog(problem.Tablets.Select(tablet => tablet.Definition), problem.Charms.Select(charm => charm.Definition));
        Assert.NotEmpty(TabletMixAdvisor.Rank(problem, catalog, 0, 0));
        problem.ScalesSide = HorizontalSide.Right;
        Assert.Empty(TabletMixAdvisor.Rank(problem, catalog, 0, 0));
    }

    [Fact]
    public void NonDiscardableArtifactsAreNeitherDiscardedNorReplaced()
    {
        var burden = Burden();
        burden.Definition.CannotDiscard = true;
        burden.Worth = new() { Base = -5, PerLevel = 0 };
        var problem = new PlacementProblem { Grid = new(1, 1, 1), Charms = { burden }, CurrentCharms = { [1] = new(0, 0) } };
        Assert.Empty(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)));
        var offer = new OfferCandidate { DefinitionId = 1144, Kind = "charm", Charm = Scales().Definition };
        Assert.False(Assert.Single(OfferAdvisor.Rank(problem, new[] { offer }, 0)).Available);
        burden.Definition.CannotDiscard = false;
        Assert.NotEmpty(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)));
        Assert.True(Assert.Single(OfferAdvisor.Rank(problem, new[] { offer }, 0)).Available);
    }

    [Fact]
    public void DirectionEffectAndPublishedResultReplayTogether()
    {
        var (snapshot, catalog, preferences) = PlanInputs();
        preferences.Combat.ScalesSide = HorizontalSide.Right;
        using var runner = new PlanRunner(catalog);
        runner.Submit(snapshot, preferences, "scales-catalog");
        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(10)), runner.State.Error);
        var replay = runner.CaptureReplay();
        Assert.NotNull(replay);
        var restored = System.Text.Json.JsonSerializer.Deserialize<PlanReplay>(JsonConvert.SerializeObject(replay))!;
        var result = restored.Rebuild();
        Assert.Equal(HorizontalSide.Right, restored.Preferences!.Combat!.ScalesSide);
        Assert.Contains("오른쪽", Assert.Single(result.PositionDetails));
        Assert.Empty(restored.Expected!.Differences(ReplayResult.From(result)));
    }

    [Theory]
    [InlineData(HorizontalSide.Automatic)]
    [InlineData(HorizontalSide.Left)]
    [InlineData(HorizontalSide.Right)]
    public void TabletAndSideSearchMatchesAllSmallBoardArrangements(HorizontalSide side)
    {
        var problem = ScalesProblem();
        problem.ScalesSide = side;
        problem.Charms.Add(new()
        {
            InstanceId = 2,
            Definition = new()
            {
                MaxLevel = 1,
                Combat = new() { Collected = true, Stats = { new() { Key = "FIREDAMAGE", Values = new() { 0, 30 } } } }
            }
        });
        problem.CurrentCharms[2] = new(1, 0);
        var tablet = new TabletSlot { InstanceId = 9, Definition = new() { Query = "LEFT 1\nRIGHT 2", IsRotatable = true } };
        problem.Tablets.Add(tablet);
        problem.CurrentTablets[9] = new(new(2, 0), 0);
        var current = PlacementSolver.Score(problem, new[] { tablet.At(new(2, 0), 0) }, problem.CurrentCharms);
        problem.Combat = CombatPlanning.Capture(problem, current, Snapshot("FIREDAMAGE"), new() { ScalesSide = side });
        var optimum = double.NegativeInfinity;
        for (var t = 0; t < 6; t++)
            for (var r = 0; r < 4; r++)
                for (var a = 0; a < 6; a++)
                    for (var b = 0; b < 6; b++)
                    {
                        if (a == b || a == t || b == t) continue;
                        var trial = PlacementSolver.Score(problem, new[] { tablet.At(new(t, 0), r) },
                            new Dictionary<int, GridPos> { [1] = new(a, 0), [2] = new(b, 0) });
                        if (trial.UnpositionedCharms.Count == 0) optimum = Math.Max(optimum, trial.Score);
                    }
        var result = PlacementSolver.Solve(problem);
        Assert.Empty(result.UnpositionedCharms);
        Assert.Equal(optimum, result.Score);
    }

    [Fact]
    public void MultipleScalesReportSideCapacityConflicts()
    {
        var problem = new PlacementProblem { Grid = new(6, 1, 6), ScalesSide = HorizontalSide.Right };
        for (var i = 1; i <= 4; i++)
        {
            var charm = Scales();
            charm.InstanceId = i;
            problem.Charms.Add(charm);
            problem.CurrentCharms[i] = new(i - 1, 0);
        }
        var result = PlacementSolver.Solve(problem);
        Assert.Single(result.UnpositionedCharms);
        Assert.Equal(4, result.CharmPositions.Count);
        Assert.Empty(result.InactiveCharms);
    }

    private static CharmSlot Burden() => new()
    {
        InstanceId = 1,
        Definition = new() { EntityId = 1304, Id = "MindBurden", MaxLevel = 0, Behavior = "Charm_StatusInstance", HasNoActivationEffect = true, Combat = new() { Collected = true } }
    };

    private static CharmSlot Scales() => new()
    {
        InstanceId = 1,
        Definition = new()
        {
            EntityId = 1144,
            Id = "FireIce",
            Names = { ["current"] = "대립의 천칭" },
            MaxLevel = 2,
            Behavior = "Charm_FireIce",
            HorizontalStats = new() { LeftStat = "FIREDAMAGE", RightStat = "ICEDAMAGE", MainByLevel = new() { 2, 7, 11 }, OppositeByLevel = new() { 0, 1, 4 } },
            Combat = new() { Collected = true }
        }
    };

    private static PlacementProblem ScalesProblem() => new()
    { Grid = new(6, 1, 6), Charms = { Scales() }, CurrentCharms = { [1] = new(0, 0) } };

    private static void Capture(PlacementProblem problem, string stat)
    {
        var current = PlacementSolver.Score(problem, [], problem.CurrentCharms);
        problem.Combat = CombatPlanning.Capture(problem, current, Snapshot(stat), new() { ScalesSide = problem.ScalesSide });
    }

    private static CombatSnapshot Snapshot(string stat) => new()
    {
        ObservedStats = { [stat] = 100 },
        WeaponAttacks = { new() { Id = "test", DamageKind = CombatDamageKind.Weapon, Action = CombatActionKind.Basic, Stat = stat, ElementFromRelatedStat = true } }
    };

    private static (GameSnapshot Snapshot, Catalog Catalog, PlanPreferences Preferences) PlanInputs() =>
        (new()
        {
            Run = new() { Combat = Snapshot("FIREDAMAGE") },
            Inventory = new()
            {
                Width = 6,
                Height = 1,
                Storage = 6,
                LevelMatrix = new(),
                DisabledCells = new(),
                Items = { new() { InstanceId = 1, DefinitionId = 1144, Position = new(0, 0), IsActive = true } }
            }
        }, new Catalog([], new[] { Scales().Definition }), new() { Recommendations = false });
}
