using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public sealed class RefinementSeedTests
{
    [Fact]
    public void RefinementUsesTheIncumbentNeighborsWithinTheAssignmentBudget()
    {
        var (problem, layout, seed) = Inputs();
        var options = new SolverOptions { FixpointIterations = 1, PolishPasses = 0 };
        var original = seed.ToArray();
        var fresh = PlacementSolver.EvaluateLayouts(problem, new[] { layout }, options);

        var continued = PlacementSolver.RefineLayouts(problem, new[] { layout }, seed, options);

        Assert.True(continued.Score > fresh.Score);
        Assert.True(PriorityPlacement.Compare(continued, PlacementSolver.Score(problem, layout, seed)) >= 0);
        Assert.Equal(continued.Score, PlacementSolver.Score(problem, layout, continued.CharmPositions).Score, 8);
        Assert.Equal(original, seed.ToArray());
        Assert.Empty(problem.CurrentCharms);
        Assert.Empty(problem.PlannedCharms);
        Assert.Equal(problem.Charms.Count, continued.CharmPositions.Values.Distinct().Count());
    }

    [Fact]
    public void RefinementKeepsTheFreshResultAvailable()
    {
        var (problem, layout, seed) = Inputs();
        var options = new SolverOptions();
        var fresh = PlacementSolver.EvaluateLayouts(problem, new[] { layout }, options);

        var continued = PlacementSolver.RefineLayouts(problem, new[] { layout }, seed, options);

        Assert.True(PriorityPlacement.Compare(continued, fresh) >= 0);
    }

    [Theory]
    [InlineData("tablet")]
    [InlineData("duplicate")]
    [InlineData("outside")]
    [InlineData("missing")]
    [InlineData("unknown")]
    public void InvalidSeedsUseFreshAssignment(string invalid)
    {
        var (problem, layout, seed) = Inputs();
        switch (invalid)
        {
            case "tablet": seed[1] = layout[0].Position; break;
            case "duplicate": seed[1] = seed[2]; break;
            case "outside": seed[1] = new GridPos(0, 4); break;
            case "missing": seed.Remove(1); break;
            case "unknown": seed[99] = seed[1]; seed.Remove(1); break;
        }
        var options = new SolverOptions();
        var fresh = PlacementSolver.EvaluateLayouts(problem, new[] { layout }, options);

        var continued = PlacementSolver.RefineLayouts(problem, new[] { layout }, seed, options);

        Assert.Equal(fresh.Score, continued.Score);
        Assert.Equal(fresh.CharmPositions.OrderBy(pair => pair.Key), continued.CharmPositions.OrderBy(pair => pair.Key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefinementPreservesImmovableItems(bool displacedSeed)
    {
        var (problem, layout, seed) = Inputs();
        var pinned = seed[1];
        problem.Charms[0].Immovable = true;
        problem.CurrentCharms[1] = pinned;
        if (displacedSeed) (seed[1], seed[2]) = (seed[2], seed[1]);
        var options = new SolverOptions();
        var fresh = PlacementSolver.EvaluateLayouts(problem, new[] { layout }, options);

        var continued = PlacementSolver.RefineLayouts(problem, new[] { layout }, seed, options);

        Assert.Equal(pinned, continued.CharmPositions[1]);
        Assert.True(PriorityPlacement.Compare(continued, fresh) >= 0);
        Assert.DoesNotContain(layout[0].Position, continued.CharmPositions.Values);
    }

    private static (PlacementProblem Problem, List<TabletPlacement> Layout, Dictionary<int, GridPos> Seed) Inputs()
    {
        // 게임 카탈로그가 아닌 합성 입력으로 이웃 의존 배정의 출발점 손실을 재현한다.
        var problem = new PlacementProblem { Grid = new GridSpec(5, 4, 20) };
        for (var i = 0; i < 14; i++)
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = i + 1,
                Definition = new CharmDefinition
                {
                    Id = "synthetic" + i,
                    MaxLevel = 5,
                    NeighborEnhanceCategory = i == 0 ? "TEST" : "",
                    IsSummonPlanet = i > 0 && i < 8,
                    Categories = i > 0 && i < 8 ? new List<string> { "TEST" } : new List<string>(),
                },
                Worth = new CharmWorth { Base = 1, PerLevel = i == 0 ? 0 : 1 + i * 0.2 },
            });
        }
        var tablet = new TabletSlot { InstanceId = 100, Definition = new TabletDefinition { Query = "LEFT 2\nUP 1\nUPUP 3" } };
        problem.Tablets.Add(tablet);
        var layout = new List<TabletPlacement> { tablet.At(new GridPos(4, 3), 0) };
        GridPos[] cells =
        {
            new(1, 0), new(1, 1), new(0, 1), new(4, 1), new(3, 0), new(0, 0), new(0, 2),
            new(1, 2), new(3, 1), new(3, 2), new(4, 0), new(4, 2), new(2, 2), new(1, 3),
        };
        var seed = problem.Charms.Select((charm, index) => (charm.InstanceId, Cell: cells[index]))
            .ToDictionary(pair => pair.InstanceId, pair => pair.Cell);
        return (problem, layout, seed);
    }
}
