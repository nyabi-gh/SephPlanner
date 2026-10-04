using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 가방 읽기(<c>GameReader</c>)와 적용기의 사전 검증(<c>PlanApplier</c>)이 같은 번호를 내야 한다. 둘 다
/// 플러그인이라 CI 가 빌드하지 못하므로 규칙은 여기서 붙잡는다.
/// </summary>
public class ItemIdentityTests
{
    private static readonly GridSpec Grid = new GridSpec(6, 7, 41);

    [Fact]
    public void GameNumbersPassThrough()
    {
        Assert.Equal(12345, ItemIdentity.Of(12345, Grid, 3, 4));
    }

    [Fact]
    public void UnnumberedItemsTakeANegativeNumberFromTheirCell()
    {
        Assert.Equal(-(Grid.ToIndex(0, 0) + 1), ItemIdentity.Of(0, Grid, 0, 0));
        Assert.Equal(-(Grid.ToIndex(5, 6) + 1), ItemIdentity.Of(0, Grid, 5, 6));
    }

    [Fact]
    public void UnnumberedItemsInDifferentCellsDoNotCollide()
    {
        var seen = new HashSet<int>();
        for (var y = 0; y < Grid.Height; y++)
            for (var x = 0; x < Grid.Width; x++)
            {
                var identity = ItemIdentity.Of(0, Grid, x, y);
                Assert.True(identity < 0, $"({x},{y}) -> {identity}");
                Assert.True(seen.Add(identity), $"({x},{y}) 가 다른 칸과 같은 번호 {identity}");
            }
    }
}
