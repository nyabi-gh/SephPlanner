using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 칸 안팎을 가리는 판정.
///
/// 예전에는 이 판정이 네 벌로 흩어져 있었고(계획, 적용 전 검사, 적용기, 읽기) 그중 읽기 쪽만
/// 아직 잠긴 칸을 보지 않았다. 넷이 갈리면 솔버와 자동 배치가 서로 다른 격자를 보게 되므로
/// 한 벌로 모았고, 여기가 그 한 벌을 지킨다.
/// </summary>
public class GridSpecTests
{
    /// <summary>6x7 판에서 두 줄만 열린 상태. 런 초반이 늘 이렇다.</summary>
    private static readonly GridSpec TwoRowsOpen = new GridSpec(6, 7, 12);

    [Fact]
    public void CellsInsideTheOpenAreaAreContained()
    {
        Assert.True(TwoRowsOpen.Contains(0, 0));
        Assert.True(TwoRowsOpen.Contains(5, 1));
        Assert.True(TwoRowsOpen.Contains(new GridPos(3, 1)));
    }

    [Fact]
    public void CellsBeyondStorageAreNotContained()
    {
        // 격자 안이지만 아직 열리지 않은 칸이다. 여기를 빠뜨리면 솔버가 잠긴 칸에 물건을 놓으라고 한다.
        Assert.False(TwoRowsOpen.Contains(0, 2));
        Assert.False(TwoRowsOpen.Contains(new GridPos(5, 6)));
    }

    [Fact]
    public void CoordinatesOutsideTheGridAreNotContained()
    {
        // 게임이 포션 벨트를 같은 딕셔너리의 y=100 줄에 둔다. 격자와 아무 상관이 없는 자리다.
        Assert.False(TwoRowsOpen.Contains(0, 100));
        Assert.False(TwoRowsOpen.Contains(6, 0));
        Assert.False(TwoRowsOpen.Contains(-1, 0));
        Assert.False(TwoRowsOpen.Contains(new GridPos(0, -1)));
    }

    [Fact]
    public void AFullyOpenGridContainsEveryCell()
    {
        var full = GridSpec.WithStorage(GridSpec.DefaultWidth * GridSpec.DefaultHeight);

        for (var index = 0; index < full.Storage; index++)
            Assert.True(full.Contains(full.ToPosition(index)));
    }
}
