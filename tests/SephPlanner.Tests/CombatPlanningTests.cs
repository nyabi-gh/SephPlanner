using System.Diagnostics;
using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;
using Xunit.Abstractions;

namespace SephPlanner.Tests;

public sealed class CombatPlanningTests(ITestOutputHelper output)
{
    [Fact]
    public void BuildPriorityMustBeEnabledBeforeSavedComboPreferencesCanBeatDps()
    {
        var key = Charm(1, "PHYSICALDAMAGE", new() { 0 }).Definition;
        key.LineCategories = new() { "FIRE", "ICE" };
        key.ContextStats.Add(new()
        {
            Source = StatCountSource.RowCategory,
            Category = "ICE",
            CombatKey = "PHYSICALDAMAGE",
            AmountByLevel = new() { 100 }
        });
        var catalog = new Catalog(Array.Empty<TabletDefinition>(), new[] { key }, new[]
        {
            new ComboDefinition { Id = "FIRE", Combat = new() { Collected = true } },
            new ComboDefinition { Id = "ICE", Combat = new() { Collected = true } }
        });
        var snapshot = new GameSnapshot
        {
            Run = new() { Combat = Snapshot(CombatActionKind.Basic) },
            Inventory = new()
            {
                Width = 1,
                Height = 2,
                Storage = 2,
                ComboCounts = new() { ["FIRE"] = 1 },
                Items = { new() { InstanceId = 1, DefinitionId = 1, Position = new(0, 0), IsActive = true } }
            }
        };
        var preferences = new PlanPreferences { Recommendations = false, PriorityCategories = new() { "FIRE" } };
        var dps = PlanBuilder.Build(snapshot, catalog, preferences)!;
        Assert.Equal(200, dps.Best.Score);
        Assert.Equal(new GridPos(0, 1), dps.Best.CharmPositions[1]);
        Assert.False(dps.PrioritizeBuild);
        Assert.Empty(dps.UnsupportedChangeWarnings);
        Assert.Equal(200, dps.Best.Combat!.EmptyStartDps);
        preferences.Combat.PrioritizeBuild = true;
        var build = PlanBuilder.Build(snapshot, catalog, preferences)!;
        Assert.Equal(100, build.Best.Score);
        Assert.Equal(new GridPos(0, 0), build.Best.CharmPositions[1]);
        Assert.True(build.PrioritizeBuild);
    }

    [Fact]
    public void FavoritesAndCategoryPreferencesOnlyRankAheadWhenBuildModeIsEnabled()
    {
        var problem = new PlacementProblem { Grid = new(3, 1, 3) };
        problem.Charms.Add(Charm(1, "PHYSICALDAMAGE", new() { 0 }));
        problem.CurrentCharms[1] = new(0, 0);
        var current = PlacementSolver.Score(problem, Array.Empty<TabletPlacement>(), problem.CurrentCharms);
        problem.Combat = CombatPlanning.Capture(problem, current, Snapshot(CombatActionKind.Basic), new());
        var strong = Charm(2, "PHYSICALDAMAGE", new() { 100 }).Definition;
        var preferred = Charm(3, "PHYSICALDAMAGE", new() { 1 }).Definition;
        preferred.Categories.Add("FIRE");
        var candidates = new[]
        {
            new OfferCandidate { Kind = "charm", DefinitionId = 2, Charm = strong },
            new OfferCandidate { Kind = "charm", DefinitionId = 3, Charm = preferred }
        };
        var dps = OfferAdvisor.Rank(problem, candidates, 0, priorityCategories: new[] { "FIRE" }, presetCharms: new[] { 3 });
        Assert.Equal(2, dps[0].Candidate.DefinitionId);
        Assert.All(dps, item => { Assert.False(item.MatchesPreset); Assert.False(item.MatchesPriority); });
        problem.Combat.Scenario.PrioritizeBuild = true;
        var build = OfferAdvisor.Rank(problem, candidates, 0, priorityCategories: new[] { "FIRE" }, presetCharms: new[] { 3 });
        Assert.Equal(3, build[0].Candidate.DefinitionId);
        Assert.True(build[0].Gain < build[1].Gain);
    }

    [Theory]
    [InlineData(CombatActionKind.Basic, 140, 1)]
    [InlineData(CombatActionKind.Dash, 300, 2)]
    public void SolverMatchesExhaustiveDpsAndIgnoresLegacyWeights(CombatActionKind action, double expected, int boosted)
    {
        var problem = new PlacementProblem { Grid = new(3, 1, 3) };
        problem.Charms.Add(Charm(1, "PHYSICALDAMAGE", new() { 0, 40 }));
        problem.Charms.Add(Charm(2, "DASHATTACKDAMAGEBONUS", new() { 0, 200 }));
        problem.Charms[0].Weight = 1000;
        problem.Charms[1].Weight = 0.001;
        problem.FixedEffects.Add(new() { Position = new(0, 0), Level = 1 });
        problem.CurrentCharms[1] = new(1, 0);
        problem.CurrentCharms[2] = new(2, 0);
        var current = PlacementSolver.Score(problem, Array.Empty<TabletPlacement>(), problem.CurrentCharms);
        problem.Combat = CombatPlanning.Capture(problem, current, Snapshot(action), new() { WeaponSequence = new() { action } });
        var optimum = double.MinValue;
        for (var a = 0; a < 3; a++)
            for (var b = 0; b < 3; b++)
                if (a != b) optimum = Math.Max(optimum, PlacementSolver.Score(problem, Array.Empty<TabletPlacement>(),
                    new Dictionary<int, GridPos> { [1] = new(a, 0), [2] = new(b, 0) }).Score);
        var solved = PlacementSolver.Solve(problem);
        Assert.Equal(expected, optimum);
        Assert.Equal(optimum, solved.Score);
        Assert.Equal(new GridPos(0, 0), solved.CharmPositions[boosted]);
        Assert.Equal(solved.Score, solved.Combat!.Dps);
    }

    [Fact]
    public void OffersUseWholeLoadoutDpsWithoutComboPointBonuses()
    {
        var problem = new PlacementProblem { Grid = new(3, 1, 3) };
        problem.Charms.Add(Charm(1, "PHYSICALDAMAGE", new() { 0 }));
        problem.CurrentCharms[1] = new(0, 0);
        var current = PlacementSolver.Score(problem, Array.Empty<TabletPlacement>(), problem.CurrentCharms);
        var combo = new ComboDefinition
        {
            Id = "FIRE",
            Thresholds = { 1 },
            Combat = new()
            {
                Collected = true,
                Stats = { new() { Key = "ALLDAMAGEBONUS", Threshold = 1, Values = new() { 20 } } }
            }
        };
        problem.Combos = _ => combo;
        problem.ComboCounts = new Dictionary<string, int>();
        problem.Combat = CombatPlanning.Capture(problem, current, Snapshot(CombatActionKind.Basic), new());
        var candidate = Charm(2, "PHYSICALDAMAGE", new() { 10 }).Definition;
        candidate.Categories.Add("FIRE");
        var advice = Assert.Single(OfferAdvisor.Rank(problem, new[] { new OfferCandidate { DefinitionId = 2, Charm = candidate, Kind = "charm" } }, 0,
            comboCounts: problem.ComboCounts, combos: problem.Combos));
        Assert.Equal(32, advice.Gain);
        Assert.Equal(0, advice.ComboBonus);
    }

    [Theory]
    [InlineData(30, 16, 4)]
    [InlineData(42, 35, 6)]
    public void RepresentativeSearchReportsMeasuredRuntimeAndKeepsCurrentOrBetterDps(int storage, int charmCount, int tabletCount)
    {
        var problem = new PlacementProblem { Grid = new(6, 7, storage) };
        var queries = new[] { "RIGHT 1", "HORIZONTAL 2", "UP 1", "O 2", "VERTICAL 1", "KNIGHTUPLEFT 2" }.Take(tabletCount).ToArray();
        for (var i = 0; i < queries.Length; i++)
        {
            problem.Tablets.Add(new() { InstanceId = 900 + i, Definition = new() { EntityId = 900 + i, Query = queries[i] } });
            problem.CurrentTablets[900 + i] = new(problem.Grid.ToPosition(i), 0);
        }
        for (var i = 0; i < charmCount; i++)
        {
            var charm = Charm(i + 1, i % 2 == 0 ? "PHYSICALDAMAGE" : "ALLDAMAGEBONUS", new() { 0, 2, 4, 6, 8, 10 });
            problem.Charms.Add(charm);
            problem.CurrentCharms[charm.InstanceId] = problem.Grid.ToPosition(i + queries.Length);
        }
        var layout = problem.Tablets.Select(tablet => tablet.At(problem.CurrentTablets[tablet.InstanceId].Position, 0)).ToList();
        var current = PlacementSolver.Score(problem, layout, problem.CurrentCharms);
        problem.Combat = CombatPlanning.Capture(problem, current, Snapshot(CombatActionKind.Basic), new());
        current = PlacementSolver.Score(problem, layout, problem.CurrentCharms);
        var memory = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var result = PlacementSolver.Solve(problem);
        timer.Stop();
        output.WriteLine($"DPS 탐색: {storage}칸, 아티팩트 {charmCount}개, 석판 {tabletCount}개, {timer.Elapsed.TotalMilliseconds:0}ms, 할당 {(GC.GetAllocatedBytesForCurrentThread() - memory) / 1048576d:0.0}MiB, {current.Score} → {result.Score}");
        Assert.True(result.Score >= current.Score);
        Assert.Equal(0, result.UnplacedTablets);
    }

    private static CharmSlot Charm(int id, string key, List<int> values) => new()
    {
        InstanceId = id,
        Definition = new()
        {
            Id = "c" + id,
            EntityId = id,
            Combat = new()
            {
                Collected = true,
                Stats = { new() { Key = key, Values = values } }
            }
        },
    };

    private static CombatSnapshot Snapshot(CombatActionKind action) => new()
    {
        ObservedStats = { ["PHYSICALDAMAGE"] = 100 },
        WeaponAttacks = { new() { Id = "weapon", Action = action, DamageKind = CombatDamageKind.Weapon } },
    };
}
