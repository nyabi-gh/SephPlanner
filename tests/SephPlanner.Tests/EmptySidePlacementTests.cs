using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class EmptySidePlacementTests
{
    [Fact]
    public void BothNeighborsAreClearedTogetherToActivateTheHighValueCharm()
    {
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(6, 1, 6),
            FixedEffects = { new FixedEffectCell { Position = new GridPos(2, 0), Level = 4 } },
        };
        for (var id = 1; id <= 4; id++)
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Definition = new CharmDefinition
                {
                    EntityId = id,
                    MaxLevel = 4,
                    CriteriaType = id == 1 ? "CharmActivateCriteria_BothSidesAreEmpty" : "",
                },
                Worth = new CharmWorth { Base = 1, PerLevel = id == 1 ? 10 : 1 },
            });
            problem.CurrentCharms[id] = new GridPos(id - 1, 0);
        }
        var layout = new List<List<TabletPlacement>> { new() };
        var before = PlacementSolver.EvaluateLayouts(problem, layout, new SolverOptions { EmptySideTrials = 0 });
        var after = PlacementSolver.EvaluateLayouts(problem, layout);
        Assert.True(after.Score > before.Score);
        Assert.Equal(new GridPos(2, 0), after.CharmPositions[1]);
        Assert.DoesNotContain(new GridPos(1, 0), after.CharmPositions.Values);
        Assert.DoesNotContain(new GridPos(3, 0), after.CharmPositions.Values);
        Assert.Equal(4, after.CharmPositions.Values.Distinct().Count());
        Assert.DoesNotContain(1, after.InactiveCharms);
        Assert.Equal(after.Score, PlacementSolver.Score(problem, after.Tablets, after.CharmPositions).Score, 6);
    }
}
