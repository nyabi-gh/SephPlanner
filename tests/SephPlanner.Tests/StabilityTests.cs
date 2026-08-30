using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 상황이 실질적으로 달라지지 않았으면 제안도 달라지면 안 된다. 이득이 없는데 물건을 옮기라고
/// 하면 사용자는 그 제안을 믿지 못하게 된다.
/// </summary>
public class StabilityTests
{
    [Fact]
    public void TwoEquallyGoodCharmsAreNotAskedToSwapPlaces()
    {
        // 석판이 양옆 두 칸에 똑같이 +1 을 준다. 두 아티팩트가 이미 그 두 칸에 있으니
        // 서로 자리를 바꿔도 점수는 같다. 그래도 옮기라고 해서는 안 된다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 1,
            Definition = new TabletDefinition { Id = "T", Query = "LEFT 1\nRIGHT 1" },
        });
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });
        problem.Charms.Add(new CharmSlot { InstanceId = 11, Definition = new CharmDefinition { MaxLevel = 5 } });

        // 배정기가 자연히 고르는 순서와 반대로 놓여 있어야 실제로 맞바꾸기가 생긴다.
        problem.CurrentTablets[1] = new TabletSpot(new GridPos(1, 0), 0);
        problem.CurrentCharms[10] = new GridPos(2, 0);
        problem.CurrentCharms[11] = new GridPos(0, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(2, 0), arrangement.CharmPositions[10]);
        Assert.Equal(new GridPos(0, 0), arrangement.CharmPositions[11]);
    }

    [Fact]
    public void AGenuineImprovementIsStillProposed()
    {
        // 안정성 때문에 진짜 이득까지 놓치면 안 된다. 지금은 아무도 좋은 칸에 있지 않다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 1,
            Definition = new TabletDefinition { Id = "T", Query = "RIGHT 3" },
        });
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });

        problem.CurrentTablets[1] = new TabletSpot(new GridPos(0, 0), 0);
        problem.CurrentCharms[10] = new GridPos(4, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(1, 0), arrangement.CharmPositions[10]);
        Assert.Equal(3, arrangement.Score, 2);
    }
}
