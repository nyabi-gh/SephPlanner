using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class PlacementObjectiveTests
{
    private static PlacementProblem MixedBoard(int storage = 9)
    {
        var p = new PlacementProblem { Grid = storage == 9 ? new GridSpec(3, 3, 9) : GridSpec.WithStorage(storage) };
        p.Tablets.Add(new TabletSlot
        {
            InstanceId = 10,
            Definition = new TabletDefinition { Query = "DIAUPLEFT -1\nUP -1\nDOWN 3" },
        });
        p.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition { EntityId = 1, MaxLevel = 2 },
            Worth = new CharmWorth { ByLevel = new[] { -0.4, -0.6, -0.8 } },
        });
        p.Charms.Add(new CharmSlot { InstanceId = 2, Definition = new CharmDefinition { EntityId = 2, MaxLevel = 1 } });
        return p;
    }

    [Theory]
    [InlineData(9)]
    [InlineData(38)]
    public void NormalSpacePreservesMixedStatsAndAvoidsNeedlessNegativeCells(int storage)
    {
        var p = MixedBoard(storage);
        var solved = PlacementSolver.Solve(p);
        Assert.Empty(solved.UnpreservedCharms);
        Assert.Empty(solved.InactiveCharms);
        Assert.Equal(0, solved.UnsafeEmptyCells);
        Assert.Equal(1.6, solved.Score, 8);
        var current = solved.TabletPositions[10];
        p.CurrentTablets[10] = current;
        foreach (var pair in solved.CharmPositions) p.CurrentCharms[pair.Key] = pair.Value;
        var repeated = PlacementSolver.Solve(p);
        Assert.Equal(solved.TabletPositions[10].Position, repeated.TabletPositions[10].Position);
        Assert.Equal(solved.TabletPositions[10].Rotation, repeated.TabletPositions[10].Rotation);
        Assert.All(solved.CharmPositions, pair => Assert.Equal(pair.Value, repeated.CharmPositions[pair.Key]));
    }

    [Fact]
    public void DeactivationPermissionAllowsTheTradeoffButRetentionStillWins()
    {
        var p = MixedBoard();
        p.Charms[0].AllowDeactivation = true;
        var allowed = PlacementSolver.Solve(p);
        Assert.Contains(1, allowed.InactiveCharms);
        Assert.Equal(2, allowed.Score, 8);
        p.Charms[0].Retained = true;
        var retained = PlacementSolver.Solve(p);
        Assert.DoesNotContain(1, retained.InactiveCharms);
        Assert.Empty(retained.UnretainedCharms);
    }

    [Fact]
    public void YieldingACharmDoesNotAuthorizeDeactivation()
    {
        var p = MixedBoard();
        p.Charms[0].Weight = 0.1;
        Assert.DoesNotContain(1, PlacementSolver.Solve(p).InactiveCharms);
    }

    [Fact]
    public void AnotherActivationCannotReplaceAnAlreadyActiveProtectedItem()
    {
        var p = MixedBoard();
        p.Tablets.Clear();
        p.Grid = new GridSpec(2, 1, 2);
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(1, 0), Level = -1 });
        p.CurrentCharms[1] = new GridPos(0, 0);
        p.CurrentCharms[2] = new GridPos(1, 0);
        var solved = PlacementSolver.Solve(p);
        Assert.DoesNotContain(1, solved.InactiveCharms);
        Assert.Contains(2, solved.InactiveCharms);
        Assert.Empty(solved.UnapprovedDeactivations);
    }

    [Fact]
    public void AnOfferCannotDisableAnExistingItemWithoutPermission()
    {
        var p = MixedBoard();
        p.Tablets.Clear();
        p.Charms.RemoveAt(1);
        p.Grid = new GridSpec(2, 1, 2);
        p.CurrentCharms[1] = new GridPos(0, 0);
        var offer = new OfferCandidate { Tablet = new TabletDefinition { Query = "HORIZONTAL -10" } };
        Assert.False(OfferAdvisor.Rank(p, new[] { offer }, 100)[0].Available);
        p.Charms[0].AllowDeactivation = true;
        Assert.True(OfferAdvisor.Rank(p, new[] { offer }, 100)[0].Available);
    }

    [Fact]
    public void NonMonotoneLevelsAreEvaluatedIndividually()
    {
        var p = new PlacementProblem { Grid = new GridSpec(3, 1, 3) };
        p.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition { MaxLevel = 2 },
            Worth = new CharmWorth { ByLevel = new[] { 2d, 7d, 1d } },
        });
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(1, 0), Level = 1 });
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(2, 0), Level = 2 });
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(new GridPos(1, 0), solved.CharmPositions[1]);
        Assert.Equal(7, solved.Score);
    }

    [Fact]
    public void FixedEffectsMatchExhaustiveAssignmentsWithMixedPoliciesAndEnchants()
    {
        var random = new Random(7291);
        for (var sample = 0; sample < 12; sample++)
        {
            var p = new PlacementProblem { Grid = new GridSpec(6, 1, 6) };
            for (var id = 1; id <= 3; id++)
                p.Charms.Add(new CharmSlot
                {
                    InstanceId = id,
                    Enchant = random.Next(-1, 3),
                    Held = id == 1,
                    AllowDeactivation = id == 3,
                    Retained = sample % 2 == 0 && id == 2,
                    Definition = new CharmDefinition { MaxLevel = 2 },
                    Worth = new CharmWorth { ByLevel = Enumerable.Range(0, 3).Select(_ => (double)random.Next(-4, 8)).ToArray() },
                });
            for (var index = 0; index < 6; index++)
                p.FixedEffects.Add(new FixedEffectCell
                {
                    Position = new GridPos(index, 0),
                    Level = random.Next(-2, 4),
                    Multiply = index % 3,
                    Disable = index == 4 ? 1 : 0,
                    IgnoreCriteria = index == 0 ? 1 : 0,
                });
            Arrangement? optimum = null;
            for (var a = 0; a < 6; a++)
                for (var b = 0; b < 6; b++)
                    for (var c = 0; c < 6; c++)
                    {
                        if (a == b || a == c || b == c) continue;
                        var scored = PlacementSolver.Score(p, [], new Dictionary<int, GridPos>
                        { [1] = new(a, 0), [2] = new(b, 0), [3] = new(c, 0) });
                        if (optimum is null || PriorityComboPlacement.Compare(scored, optimum) > 0) optimum = scored;
                    }
            Assert.Equal(0, PriorityComboPlacement.Compare(PlacementSolver.Solve(p), optimum!));
        }
    }

    [Fact]
    public void SmallBoardMatchesEnumerationOfAllTabletsAndCharmAssignments()
    {
        var p = MixedBoard();
        Arrangement? optimum = null;
        var positions = new Dictionary<int, GridPos>();
        for (var tablet = 0; tablet < 9; tablet++)
            for (var rotation = 0; rotation < 4; rotation++)
                for (var first = 0; first < 9; first++)
                    for (var second = 0; second < 9; second++)
                    {
                        if (first == second || first == tablet || second == tablet) continue;
                        positions[1] = p.Grid.ToPosition(first);
                        positions[2] = p.Grid.ToPosition(second);
                        var scored = PlacementSolver.Score(p, new[] { p.Tablets[0].At(p.Grid.ToPosition(tablet), rotation) }, positions);
                        if (optimum is null || PriorityComboPlacement.Compare(scored, optimum) > 0) optimum = scored;
                    }
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(0, PriorityComboPlacement.Compare(solved, optimum!));
    }

    [Fact]
    public void TabletRefinementRepairsARestrictedBeamWithoutLosingItsStrength()
    {
        var p = MixedBoard();
        p.Charms.RemoveAt(0);
        p.CurrentTablets[10] = new TabletSpot(new GridPos(1, 1), 0);
        p.CurrentCharms[2] = new GridPos(1, 2);
        var solved = PlacementSolver.Solve(p, new SolverOptions { BeamWidth = 1, ExactCandidates = 1 });
        Assert.Equal(2, solved.Score, 8);
        Assert.Equal(0, solved.UnsafeEmptyCells);
        Assert.Empty(solved.InactiveCharms);
    }

    [Fact]
    public void UsefulCentralEnhancementOutranksRemovingAnEmptyPenaltyCell()
    {
        var p = new PlacementProblem { Grid = new GridSpec(3, 3, 9) };
        for (var id = 1; id <= 4; id++)
            p.Charms.Add(new CharmSlot { InstanceId = id, Definition = new CharmDefinition { MaxLevel = 1 } });
        p.Tablets.Add(new TabletSlot
        {
            InstanceId = 10,
            Definition = new TabletDefinition { Query = "LEFT 1\nRIGHT 1\nUP 1\nDOWN 1\nDIAUPLEFT -1" },
        });
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(new GridPos(1, 1), solved.TabletPositions[10].Position);
        Assert.Equal(8, solved.Score);
        Assert.Empty(solved.InactiveCharms);
        Assert.Equal(1, solved.UnsafeEmptyCells);
    }

    [Fact]
    public void RetentionSurvivesScoresWithVeryDifferentScales()
    {
        var costs = new double[,] { { 1e12, 1e12 }, { 0, 0.001 } };
        var assignment = HungarianAssignment.SolvePrioritized(costs, new int[,] { { 0, 0 }, { 0, 0 } });
        Assert.Equal(new[] { 1, 0 }, assignment);
        assignment = HungarianAssignment.SolvePrioritized(costs, new int[,] { { -1, 0 }, { 0, 0 } });
        Assert.Equal(new[] { 0, 1 }, assignment);
    }

    private static PlacementProblem SupportedStats()
    {
        var p = new PlacementProblem { Grid = new GridSpec(3, 1, 3) };
        var support = new CharmDefinition
        {
            EntityId = 1,
            MaxLevel = 0,
            Behavior = "Charm_StatusInstance",
            StatWorthCoverageKnown = true,
            StatWorthByLevel = { -1 },
            StatBenefitByLevel = { 2 },
            StatPenaltyByLevel = { -3 },
            StatEffects =
            {
                new CharmStatEffect { StatusId = "MAGIC_CRITICAL", AmountByLevel = { 2 }, WorthPerUnit = 1, Samples = 1 },
                new CharmStatEffect { StatusId = "PHYSICAL_DAMAGE", AmountByLevel = { -3 }, WorthPerUnit = 1, Samples = 3 },
            },
        };
        p.Charms.Add(new CharmSlot { InstanceId = 1, Definition = support });
        p.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Weight = 4,
            Definition = new CharmDefinition { EntityId = 2, MaxLevel = 0, IsMagic = true, IsAttackable = true, UsesMagicCritical = true },
        });
        p.CurrentCharms[1] = new GridPos(0, 0);
        p.CurrentCharms[2] = new GridPos(1, 0);
        return p;
    }

    [Fact]
    public void PrimaryPreferenceWeightsKnownBenefitsAndKeepsPenalty()
    {
        var p = SupportedStats();
        var scored = PlacementSolver.Score(p, [], p.CurrentCharms);
        Assert.Equal(9, scored.Score);
        p.Charms.Add(new CharmSlot { InstanceId = 3, Definition = p.Charms[1].Definition, Weight = 4 });
        p.CurrentCharms[3] = new GridPos(2, 0);
        Assert.Equal(13, PlacementSolver.Score(p, [], p.CurrentCharms).Score);
        p.Charms[0].Weight = 0.5;
        Assert.Equal(6, PlacementSolver.Score(p, [], p.CurrentCharms).Score);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("unknown")]
    [InlineData("nonattack")]
    public void UnsupportedOrInactiveTargetsDoNotCreateSupportValue(string kind)
    {
        var p = SupportedStats();
        if (kind == "inactive") p.Charms[1].Enchant = -1;
        if (kind == "unknown") p.Charms[1].Definition.UsesMagicCritical = false;
        if (kind == "nonattack") p.Charms[1].Definition.IsAttackable = false;
        Assert.Equal(kind == "inactive" ? -1 : 3, PlacementSolver.Score(p, [], p.CurrentCharms).Score);
    }

    [Theory]
    [InlineData(0, false, 3)]
    [InlineData(1, false, 9)]
    [InlineData(1, true, -1)]
    public void ManaSupportRequiresPositiveCostAtTheActiveLevel(int cost, bool dormant, double expected)
    {
        var p = SupportedStats();
        p.Charms[0].Definition.StatEffects[0].StatusId = "MP_REGEN";
        p.Charms[1].Definition.MagicCostByLevel.AddRange(new[] { (double)cost, 5 });
        p.Charms[1].IsDormant = dormant;
        Assert.Equal(expected, PlacementSolver.Score(p, [], p.CurrentCharms).Score);
    }

    [Fact]
    public void RawStatsAndConversionEvidenceSurviveSerialization()
    {
        var charm = new CharmDefinition { EntityId = 9, MaxLevel = 1, Behavior = "Charm_StatusInstance" };
        CharmStatWorth.Apply(new[] { charm }, new StatMeasurement
        {
            CharmStats = { new CharmStatTable { EntityId = 9, StatusId = "TEST", ValuesByLevel = { -2, 2 } } },
        });
        var restored = JsonSerializer.Deserialize<CharmDefinition>(JsonSerializer.Serialize(charm))!;
        var effect = Assert.Single(restored.StatEffects);
        Assert.Equal(new[] { -2, 2 }, effect.AmountByLevel);
        Assert.Equal(0.25, effect.WorthPerUnit);
        Assert.Equal(1, effect.Samples);
    }

    [Fact]
    public void DeactivationPolicyInvalidatesPlacementAndRoundTripsInReplay()
    {
        var preferences = new PlanPreferences();
        var snapshot = new GameSnapshot();
        var before = PlanFingerprint.Placement(snapshot, preferences, "catalog");
        preferences.DeactivationAllowed.Add(9);
        Assert.NotEqual(before, PlanFingerprint.Placement(snapshot, preferences, "catalog"));
        var replay = JsonSerializer.Deserialize<ReplayPreferences>(JsonSerializer.Serialize(ReplayPreferences.From(preferences)))!;
        Assert.Contains(9, replay.Restore().DeactivationAllowed);
        replay.DeactivationAllowed = null;
        Assert.Throws<InvalidDataException>(() => replay.Restore());
    }
}
