using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class FixedEffectTests
{
    [Fact]
    public void FixedEffectsSeedTheSimulation()
    {
        // 신비 콤보의 고정 각인처럼, 질의 없이 절대 좌표에 박힌 효과가 행렬에 먼저 깔린다.
        var grid = new GridSpec(6, 4, 24);
        var fixedEffects = new List<FixedEffectCell>
        {
            new() { Position = new GridPos(1, 1), Level = 2, Multiply = 2 },
            new() { Position = new GridPos(2, 1), Disable = 1 },
        };

        var result = TabletSimulator.Run(
            new List<TabletPlacement>(), new GridOccupancy(), grid, fixedEffects);

        Assert.Equal(2, result.LevelAt(new GridPos(1, 1)));
        Assert.Equal(4, result.EffectiveLevel(new GridPos(1, 1), enchant: 0));
        Assert.True(result.IsDisabled(new GridPos(2, 1)));
    }
}
