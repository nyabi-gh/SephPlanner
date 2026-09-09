using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class ActivationReviewTests
{
    [Theory]
    [InlineData(9, false)]
    [InlineData(38, false)]
    [InlineData(38, true)]
    public void SilverBraceletIsRestoredFromANegativeCellUnlessDeactivationIsAllowed(int storage, bool allow)
    {
        var p = new PlacementProblem { Grid = storage == 9 ? new GridSpec(3, 3, 9) : GridSpec.WithStorage(storage) };
        // 게임 1.0.30 로컬 카탈로그의 은팔찌 레벨별 순가치다.
        p.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            AllowDeactivation = allow,
            Definition = new CharmDefinition
            {
                EntityId = 1194,
                Id = "SilverBracelet",
                Behavior = "Charm_StatusInstance",
                MaxLevel = 3,
                StatWorthCoverageKnown = true,
                StatWorthByLevel = { -2d / 3, -1d / 3, 0, 1d / 3 },
                Categories = { "WINDSONG" },
            },
        });
        p.Charms.Add(new CharmSlot { InstanceId = 2, Weight = 4, Definition = new CharmDefinition { MaxLevel = 1 } });
        p.Tablets.Add(new TabletSlot
        {
            InstanceId = 10,
            Definition = new TabletDefinition { Query = "DIAUPLEFT -1\nUP -1\nDOWN 3" },
        });
        p.CurrentTablets[10] = new TabletSpot(new GridPos(1, 1), 0);
        p.CurrentCharms[1] = new GridPos(1, 0);
        p.CurrentCharms[2] = new GridPos(1, 2);
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(allow, solved.InactiveCharms.Contains(1));
        Assert.DoesNotContain(2, solved.InactiveCharms);
        Assert.Empty(solved.UnpreservedCharms);
        Assert.Equal(allow ? 8 : 8 - 2d / 3, solved.Score, 8);
        if (!allow) Assert.Equal(0, solved.UnsafeEmptyCells);
    }

    private static PlacementProblem LimitedActivation()
    {
        var p = new PlacementProblem { Grid = new GridSpec(3, 1, 3) };
        p.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition { EntityId = 1, MaxLevel = 0 },
            Worth = new CharmWorth { ByLevel = new[] { -1d } },
        });
        p.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Definition = new CharmDefinition { EntityId = 2, MaxLevel = 0 },
            Worth = new CharmWorth { ByLevel = new[] { 100d } },
        });
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(1, 0), Level = -1 });
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(2, 0), Level = -1 });
        p.CurrentCharms[1] = new GridPos(0, 0);
        p.CurrentCharms[2] = new GridPos(1, 0);
        return p;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectEvaluationAndAdviceCaptureCurrentActivation(bool advice)
    {
        var p = LimitedActivation();
        var solved = advice
            ? new LayoutCache().Baseline(p, new SolverOptions())
            : PlacementSolver.EvaluateLayouts(p, new List<List<TabletPlacement>> { new() });
        Assert.DoesNotContain(1, solved.InactiveCharms);
        Assert.Contains(2, solved.InactiveCharms);
    }

    [Fact]
    public void AnInvalidAdviceBaselineCannotAuthorizeExistingActivationLoss()
    {
        var p = LimitedActivation();
        p.Charms[1].Retained = true;
        var baseline = PlacementSolver.Solve(p);
        Assert.Contains(1, baseline.UnapprovedDeactivations);
        var candidate = new OfferCandidate { Tablet = new TabletDefinition { EntityId = 10 } };
        var offer = Assert.Single(OfferAdvisor.Rank(p, new[] { candidate }, 100));
        Assert.False(offer.Available);
    }

    [Fact]
    public void ARepeatedDirectEvaluationUsesTheUpdatedPermission()
    {
        var p = LimitedActivation();
        var layouts = new List<List<TabletPlacement>> { new() };
        Assert.DoesNotContain(1, PlacementSolver.EvaluateLayouts(p, layouts).InactiveCharms);
        p.Charms[0].AllowDeactivation = true;
        var allowed = PlacementSolver.EvaluateLayouts(p, layouts);
        Assert.Contains(1, allowed.InactiveCharms);
        Assert.DoesNotContain(2, allowed.InactiveCharms);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrialEvaluationKeepsProtectionFromBeforeTabletRemoval(bool solveFirst)
    {
        var p = LimitedActivation();
        p.FixedEffects.Clear();
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Level = -1 });
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(2, 0), Level = -1 });
        p.Tablets.Add(new TabletSlot
        {
            InstanceId = 10,
            Rotatable = false,
            Definition = new TabletDefinition { Query = "HORIZONTAL 1\nLEFT -2" },
        });
        p.CurrentTablets[10] = new TabletSpot(new GridPos(2, 0), 0);
        if (solveFirst) PlacementSolver.Solve(p);
        var trial = OfferAdvisor.Clone(p);
        trial.Tablets.Clear();
        trial.CurrentTablets.Clear();
        var solved = PlacementSolver.Solve(trial);
        Assert.DoesNotContain(1, solved.InactiveCharms);
        Assert.Contains(2, solved.InactiveCharms);
        Assert.Empty(solved.UnapprovedDeactivations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MixingOtherTabletsKeepsAnExistingMixedTabletsActivation(int mixedIndex)
    {
        var p = new PlacementProblem { Grid = new GridSpec(4, 1, 4) };
        p.Charms.Add(new CharmSlot { InstanceId = 1, Enchant = -1, Retained = true });
        p.CurrentCharms[1] = new GridPos(0, 0);
        p.Tablets.Add(new TabletSlot
        {
            InstanceId = 11,
            Rotatable = false,
            Definition = new TabletDefinition { EntityId = 100, Query = "UP 1" },
        });
        p.Tablets.Add(new TabletSlot
        {
            InstanceId = 12,
            Rotatable = false,
            Definition = new TabletDefinition { EntityId = 101, Query = "UP 2" },
        });
        p.Tablets.Insert(mixedIndex, new TabletSlot
        {
            InstanceId = 10,
            Rotatable = false,
            Definition = new TabletDefinition { EntityId = TabletMix.ResultEntityId },
            InstanceQuery = "HORIZONTAL 1",
        });
        for (var i = 0; i < p.Tablets.Count; i++)
            p.CurrentTablets[p.Tablets[i].InstanceId] = new TabletSpot(new GridPos(i + 1, 0), 0);
        var catalog = new Catalog([], []);
        var advice = Assert.Single(TabletMixAdvisor.Rank(p, catalog, 0, 100));
        Assert.Equal(11, advice.InstanceA);
        Assert.Equal(12, advice.InstanceB);
        var solved = PlacementSolver.Solve(advice.Trial!);
        Assert.Empty(solved.UnretainedCharms);
        Assert.Contains(solved.Tablets, tablet => tablet.Query == "HORIZONTAL 1");
    }
}
