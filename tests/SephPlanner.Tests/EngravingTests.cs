using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 각인은 격자 위 고정된 자리에서 석판과 똑같이 효과를 내지만, 칸을 차지하지 않고 옮길 수도 없다.
/// </summary>
public class EngravingTests
{
    private static GridSpec OneRow => new(6, 7, 6);

    private static TabletPlacement Engraving(string query, int x, int y) => new()
    {
        Definition = new TabletDefinition { Id = "E", Query = query },
        Position = new GridPos(x, y),
    };

    [Fact]
    public void AnEngravingRaisesLevelsWithoutBeingPlaced()
    {
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });
        problem.FixedTablets.Add(Engraving("RIGHT 2", 0, 0));

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(3, arrangement.Score, 3);
        Assert.Empty(arrangement.Tablets);
    }

    [Fact]
    public void TheEngravingsOwnCellStaysUsable()
    {
        // 각인은 inventoryMatrix 에 들어가지 않으므로 그 자리에 아티팩트를 놓을 수 있어야 한다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 2) };
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });
        problem.Charms.Add(new CharmSlot { InstanceId = 11, Definition = new CharmDefinition { MaxLevel = 5 } });
        problem.FixedTablets.Add(Engraving("O 3", 0, 0));

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(2, arrangement.CharmPositions.Count);
        Assert.Contains(arrangement.CharmPositions.Values, position => position == new GridPos(0, 0));

        // 켜진 아티팩트 둘(2)에, 각인이 자기 칸에 준 레벨 3을 더해 5다.
        Assert.Equal(5, arrangement.Score, 3);
    }

    [Fact]
    public void EngravingsFromTheSnapshotReachTheSolver()
    {
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 6 };
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 200,
            InstanceId = 10,
            Position = new GridPos(1, 0),
            IsActive = true,
        });
        inventory.Engravings.Add(new PlacedTablet
        {
            DefinitionId = 100,
            InstanceId = 5,
            Position = new GridPos(0, 0),
            IsApplied = true,
        });

        var catalog = new Catalog(
            new[] { new TabletDefinition { Id = "E", EntityId = 100, Query = "RIGHT 2" } },
            new[] { new CharmDefinition { Id = "C", EntityId = 200, MaxLevel = 5 } });

        var plan = PlanBuilder.Build(new GameSnapshot { Inventory = inventory }, catalog);

        Assert.Equal(3, plan!.Best.Score, 3);

        // 각인은 옮길 수 없으니 이동 목록에 나와서는 안 된다.
        Assert.DoesNotContain(plan.Moves, move => move.Label == "E");
    }
}
