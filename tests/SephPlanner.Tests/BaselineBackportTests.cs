using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public sealed class BaselineBackportTests
{
    private static CharmSlot Burden() => new()
    {
        InstanceId = 1,
        Definition = new() { EntityId = 1304, MaxLevel = 0, HasNoActivationEffect = true }
    };

    [Fact]
    public void NonDiscardableArtifactCannotBeRemovedOrReplaced()
    {
        var charm = Burden();
        charm.Definition.CannotDiscard = true;
        charm.Worth = new() { Base = -5, PerLevel = 0 };
        var p = new PlacementProblem { Grid = new(1, 1, 1), Charms = { charm }, CurrentCharms = { [1] = new(0, 0) } };
        var offer = new OfferCandidate { Kind = "charm", DefinitionId = 2, Charm = new() { EntityId = 2, MaxLevel = 3 } };
        Assert.Empty(DiscardAdvisor.Rank(p, PlacementSolver.Solve(p)));
        Assert.False(Assert.Single(OfferAdvisor.Rank(p, new[] { offer }, 0)).Available);
        charm.Definition.CannotDiscard = false;
        Assert.NotEmpty(DiscardAdvisor.Rank(p, PlacementSolver.Solve(p)));
        Assert.True(Assert.Single(OfferAdvisor.Rank(p, new[] { offer }, 0)).Available);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyEffectFreesActiveSpaceUnlessExplicitlyRetained(bool retain)
    {
        var charm = Burden();
        charm.Retained = retain;
        var p = new PlacementProblem { Grid = new(2, 1, 2), Charms = { charm }, CurrentCharms = { [1] = new(0, 0) } };
        p.FixedEffects.Add(new() { Position = new(1, 0), Level = -1 });
        p.Charms.Add(new() { InstanceId = 2, Definition = new() { EntityId = 2, MaxLevel = 0 } });
        p.CurrentCharms[2] = new(1, 0);
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(new GridPos(retain ? 0 : 1, 0), solved.CharmPositions[1]);
        Assert.Empty(solved.UnretainedCharms);
        Assert.Empty(solved.UnapprovedDeactivations);
        Assert.False(charm.IsFiller);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    public void ScalesKeepTheirSideEvenWhenTheOppositeSideHasMuchHigherLevels(int start, int opposite)
    {
        var p = new PlacementProblem { Grid = new(6, 1, 6) };
        p.Charms.Add(new() { InstanceId = 1, Definition = new() { EntityId = 1144, Behavior = "Charm_FireIce", MaxLevel = 5 } });
        p.CurrentCharms[1] = new(start, 0);
        p.FixedEffects.Add(new() { Position = new(opposite, 0), Level = 5 });
        var sameSide = start == 2 ? 0 : 4;
        p.FixedEffects.Add(new() { Position = new(sameSide, 0), Level = 2 });
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(new GridPos(sameSide, 0), solved.CharmPositions[1]);
        var cache = new LayoutCache();
        var options = SolverOptions.ForAdvice(default);
        Assert.Equal(new GridPos(sameSide, 0), cache.Baseline(p, options).CharmPositions[1]);
        Assert.Equal(ScalesPosition.IsLeft(new(start, 0)), ScalesPosition.IsLeft(solved.CharmPositions[1]));
        Assert.Empty(solved.WrongSideCharms);
        var wrong = PlacementSolver.Score(p, Array.Empty<SephPlanner.Core.Tablets.TabletPlacement>(), new Dictionary<int, GridPos> { [1] = new(opposite, 0) });
        Assert.Contains(1, wrong.WrongSideCharms);
        Assert.False(ActivationPolicy.AllowsTransition(solved, wrong));
        var offer = new OfferCandidate { Kind = "charm", DefinitionId = 2, Charm = new() { EntityId = 2 } };
        var advice = Assert.Single(OfferAdvisor.Rank(p, new[] { offer }, 0));
        Assert.True(advice.Available);
        Assert.Equal(ScalesPosition.IsLeft(new(start, 0)), ScalesPosition.IsLeft(advice.Solved!.CharmPositions[1]));
        p.CurrentCharms[1] = new(opposite, 0);
        Assert.Equal(new GridPos(opposite, 0), PlacementSolver.Solve(p).CharmPositions[1]);
        Assert.Equal(new GridPos(opposite, 0), cache.Baseline(p, options).CharmPositions[1]);
    }
}
