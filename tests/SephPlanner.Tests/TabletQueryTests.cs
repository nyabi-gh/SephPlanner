using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 질의 DSL 포팅이 게임과 어긋나면 솔버 전체가 틀어진다. 게임 안에서는 플러그인의 QueryVerifier가
/// 원본과 전수 대조하지만, 게임 없이도 지켜져야 하는 규칙들을 여기에 남긴다.
/// </summary>
public class TabletQueryTests
{
    private static readonly GridSpec FullGrid = new(6, 7, 42);

    [Fact]
    public void YPlusIncludesItsOwnCellButXPlusDoesNot()
    {
        var origin = new GridPos(2, 3);

        var down = TabletQuery.Parse("Y_PLUS 1", FullGrid, origin, rotation: 0);
        var right = TabletQuery.Parse("X_PLUS 1", FullGrid, origin, rotation: 0);

        // 게임 구현이 origin.y 부터 순회한다. 비대칭이지만 그대로 따라야 한다.
        Assert.Contains(down, cell => cell.Position == origin);
        Assert.DoesNotContain(right, cell => cell.Position == origin);

        Assert.Equal(new[] { new GridPos(2, 3), new GridPos(2, 4), new GridPos(2, 5), new GridPos(2, 6) },
            down.Select(cell => cell.Position));
        Assert.Equal(new[] { new GridPos(3, 3), new GridPos(4, 3), new GridPos(5, 3) },
            right.Select(cell => cell.Position));
    }

    [Theory]
    [InlineData(0, 3, 2)]
    [InlineData(1, 2, 3)]
    [InlineData(2, 3, 4)]
    [InlineData(3, 4, 3)]
    public void OffsetTokensTurnAQuarterAtATime(int rotation, int expectedX, int expectedY)
    {
        var cells = TabletQuery.Parse("UP 1", FullGrid, new GridPos(3, 3), rotation);

        var cell = Assert.Single(cells);
        Assert.Equal(new GridPos(expectedX, expectedY), cell.Position);
    }

    [Fact]
    public void GridTokensRotateByNameRatherThanByOffset()
    {
        // 회전 1에서 X_PLUS 는 Y_MINUS 가 된다. 자기 칸 위쪽을 전부 가리킨다.
        var cells = TabletQuery.Parse("X_PLUS 1", FullGrid, new GridPos(2, 3), rotation: 1);

        Assert.Equal(new[] { new GridPos(2, 0), new GridPos(2, 1), new GridPos(2, 2) },
            cells.Select(cell => cell.Position));
        Assert.True(cells[0].BorderTop);
    }

    [Fact]
    public void BottomFollowsHowManyCellsAreOpen()
    {
        // 24칸만 열려 있으면 마지막 줄은 4행이다. 가방이 커지면 결과가 달라져야 한다.
        var cells = TabletQuery.Parse("BOTTOM 1", new GridSpec(6, 7, 24), new GridPos(0, 0), rotation: 0);

        Assert.All(cells, cell => Assert.Equal(3, cell.Position.Y));
        Assert.All(cells, cell => Assert.True(cell.BorderBottom));
        Assert.All(cells, cell => Assert.True(cell.IsYWorldPosition));
    }

    [Fact]
    public void RightEndStopsWhereTheOpenCellsStop()
    {
        // 21칸이면 마지막 열은 y=2 까지만 열려 있다.
        var cells = TabletQuery.Parse("RIGHTEND 1", new GridSpec(6, 7, 21), new GridPos(0, 0), rotation: 0);

        Assert.Equal(new[] { new GridPos(5, 0), new GridPos(5, 1), new GridPos(5, 2) },
            cells.Select(cell => cell.Position));
        Assert.True(cells[^1].BorderBottom);
    }

    [Fact]
    public void DiagonalRaysOnlyFlagTheBorderTheyRunInto()
    {
        var cells = TabletQuery.Parse("RIGHT_FALLING 1", FullGrid, new GridPos(3, 3), rotation: 0);

        Assert.Equal(new[] { new GridPos(4, 4), new GridPos(5, 5) }, cells.Select(cell => cell.Position));
        Assert.All(cells, cell => Assert.False(cell.BorderLeft));
        Assert.All(cells, cell => Assert.False(cell.BorderTop));
        Assert.True(cells[^1].BorderRight);
    }
}
