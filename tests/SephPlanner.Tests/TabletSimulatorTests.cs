using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 배수의 겹침 규칙. 게임 디컴파일로 확인한 동작이다: <c>multiplyLevelMatrix</c>에 덧셈으로
/// 쌓이고, 합이 0이면 <c>ReleasePermission</c>이 곱셈 자체를 건너뛴다.
/// </summary>
public class TabletSimulatorTests
{
    private static readonly GridSpec Grid = new(6, 7, 6);

    private static TabletPlacement Tablet(string query, int x, int y) => new()
    {
        Definition = new TabletDefinition { Query = query },
        Position = new GridPos(x, y),
    };

    [Fact]
    public void OverlappingMultipliersAddUpInsteadOfMultiplying()
    {
        // MUL/2 와 MUL/3 이 같은 칸에 걸리면 x6 이 아니라 x5 다.
        var placements = new List<TabletPlacement>
        {
            Tablet("RIGHT MUL/2", 0, 0),
            Tablet("LEFT MUL/3", 2, 0),
        };
        var fixedEffects = new List<FixedEffectCell> { new() { Position = new GridPos(1, 0), Level = 1 } };

        var result = TabletSimulator.Run(placements, new GridOccupancy(), Grid, fixedEffects);

        Assert.Equal(5, result.EffectiveLevel(new GridPos(1, 0), enchant: 0));
    }

    [Fact]
    public void MultipliersThatCancelOutLeaveTheLevelAlone()
    {
        // 합이 0이면 게임은 곱셈을 건너뛴다. x0 으로 만들어 버리면 게임과 어긋난다.
        var placements = new List<TabletPlacement>
        {
            Tablet("RIGHT MUL/2", 0, 0),
            Tablet("LEFT MUL/-2", 2, 0),
        };
        var fixedEffects = new List<FixedEffectCell> { new() { Position = new GridPos(1, 0), Level = 3 } };

        var result = TabletSimulator.Run(placements, new GridOccupancy(), Grid, fixedEffects);

        Assert.Equal(3, result.EffectiveLevel(new GridPos(1, 0), enchant: 0));
    }

    [Fact]
    public void ReusedSimulationDoesNotCarryEffectsOrApplicationStateToTheNextLayout()
    {
        var grid = new GridSpec(3, 3, 8);
        var result = new SimulationResult(grid, 0);
        var conditional = Tablet("RIGHT 2\nDOWN X\nLEFT MUL/3\nUP IGNORECRITERIA", 1, 1);
        conditional.InstanceConditionQuery = "RIGHT CHARM";
        var occupancy = new GridOccupancy();
        occupancy.AddItem(new GridPos(2, 1), isCharm: true);
        var fixedEffects = new[] { new FixedEffectCell
        {
            Position = new GridPos(2, 2), Level = -3, Disable = 1, IgnoreCriteria = 1, Multiply = 2,
        } };
        var populated = new[] { conditional, Tablet("HORIZONTAL -1", 0, 0) };
        var cases = new[]
        {
            (populated, occupancy, fixedEffects),
            (populated, new GridOccupancy(), Array.Empty<FixedEffectCell>()),
            (new[] { Tablet("RIGHT MUL/-2", 0, 0) }, occupancy, Array.Empty<FixedEffectCell>()),
            (Array.Empty<TabletPlacement>(), occupancy, Array.Empty<FixedEffectCell>()),
            (populated, occupancy, fixedEffects),
        };

        foreach (var (placements, currentOccupancy, effects) in cases)
        {
            var expected = TabletSimulator.Run(placements, currentOccupancy, grid, effects);
            TabletSimulator.RunInto(placements, currentOccupancy, result, effects);
            AssertSimulation(expected, result, grid);
        }
    }

    [Fact]
    public void PreparedQueriesFollowChangesToPlacementOverridesAndDefinitions()
    {
        var grid = new GridSpec(3, 3, 8);
        var placement = Tablet("HORIZONTAL 1", 1, 1);
        var occupancy = new GridOccupancy();
        occupancy.AddItem(new GridPos(2, 1), isCharm: true);
        var changes = new Action[]
        {
            () => { },
            () => placement.Rotation = 1,
            () => placement.Position = new GridPos(0, 1),
            () => placement.InstanceQuery = "RIGHT X\nDOWN MUL/2\nUP IGNORECRITERIA",
            () => placement.InstanceConditionQuery = "RIGHT CHARM",
            () => placement.InstanceConditionQuery = "IDX 3 PLACED",
            () => placement.InstanceQuery = null,
            () => placement.Definition.Query = "HORIZONTAL -2",
            () => placement.InstanceConditionQuery = null,
            () => placement.Definition.ConditionQuery = "RIGHT CHARM",
            () => placement.Definition = new TabletDefinition { Query = "TOP 3" },
        };

        foreach (var change in changes)
        {
            change();
            var fresh = Copy(placement);
            AssertSimulation(TabletSimulator.Run(new[] { fresh }, occupancy, grid),
                TabletSimulator.Run(new[] { placement }, occupancy, grid), grid);
        }
    }

    [Fact]
    public async Task SharedPlacementCanBeEvaluatedOnDifferentGridsConcurrently()
    {
        var placement = Tablet("TOP 1\nBOTTOM -2\nHORIZONTAL MUL/2", 1, 1);
        placement.InstanceConditionQuery = "IDX 4 PLACED";
        var grids = new[] { new GridSpec(3, 3, 8), new GridSpec(3, 3, 5), new GridSpec(4, 2, 7) };
        await Task.WhenAll(grids.Select(grid => Task.Run(() =>
        {
            var occupancy = new GridOccupancy();
            var expected = TabletSimulator.Run(new[] { Copy(placement) }, occupancy, grid);
            for (var round = 0; round < 50; round++)
                AssertSimulation(expected, TabletSimulator.Run(new[] { placement }, occupancy, grid), grid);
        })));
    }

    private static TabletPlacement Copy(TabletPlacement placement) => new()
    {
        Definition = placement.Definition,
        Position = placement.Position,
        Rotation = placement.Rotation,
        InstanceQuery = placement.InstanceQuery,
        InstanceConditionQuery = placement.InstanceConditionQuery,
    };

    private static void AssertSimulation(SimulationResult expected, SimulationResult actual, GridSpec grid)
    {
        Assert.Equal(expected.Applied, actual.Applied);
        for (var y = -1; y <= grid.Height; y++)
            for (var x = -1; x <= grid.Width; x++)
            {
                var cell = new GridPos(x, y);
                Assert.Equal(expected.LevelAt(cell), actual.LevelAt(cell));
                Assert.Equal(expected.IsDisabled(cell), actual.IsDisabled(cell));
                Assert.Equal(expected.IgnoreCriteriaAt(cell), actual.IgnoreCriteriaAt(cell));
                Assert.Equal(expected.MultiplierAt(cell), actual.MultiplierAt(cell));
                Assert.Equal(expected.EffectiveLevel(cell, -1), actual.EffectiveLevel(cell, -1));
                Assert.Equal(expected.EffectiveLevel(cell, 2), actual.EffectiveLevel(cell, 2));
            }
    }
}
