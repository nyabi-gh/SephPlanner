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
}
