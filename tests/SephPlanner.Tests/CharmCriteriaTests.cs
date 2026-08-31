using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 배치 조건 판정은 게임 <c>CharmActivateCriteria</c> 각 클래스의 <c>GetCriteria</c>를
/// 디컴파일해 그대로 옮긴 것이다. 게임 쪽에 폭 6 전제의 하드코딩(5, 6, 7)이 섞여 있는데,
/// 그것까지 똑같아야 게임과 같은 판정이 나온다. 이 테스트는 그 동작을 고정한다.
/// </summary>
public class CharmCriteriaTests
{
    private static readonly GridSpec Full = GridSpec.WithStorage(42);

    private static bool Satisfied(CharmCriteriaKind kind, int x, int y, GridSpec grid, GridOccupancy? occupancy = null) =>
        CharmCriteria.IsSatisfied(kind, new GridPos(x, y), grid, occupancy ?? new GridOccupancy());

    [Fact]
    public void SideEndUsesTheGamesHardcodedColumns()
    {
        // 게임 코드가 x == 0 || x == 5 를 하드코딩한다 (CharmActivateCriteria_SideEnd).
        Assert.True(Satisfied(CharmCriteriaKind.SideEnd, 0, 3, Full));
        Assert.True(Satisfied(CharmCriteriaKind.SideEnd, 5, 3, Full));
        Assert.False(Satisfied(CharmCriteriaKind.SideEnd, 3, 3, Full));
    }

    [Fact]
    public void BottomRowFollowsStorageNotGridHeight()
    {
        // 게임은 storage - 6 이상을 마지막 줄로 본다. 두 줄만 열렸으면 y=1 이 바닥이다.
        var twoRows = GridSpec.WithStorage(12);
        Assert.True(Satisfied(CharmCriteriaKind.BottomInInventory, 2, 1, twoRows));
        Assert.False(Satisfied(CharmCriteriaKind.BottomInInventory, 2, 0, twoRows));
    }

    [Fact]
    public void InsideNeedsARowBelowWithinStorage()
    {
        // 게임은 index + 7 <= storage - 1 로 "아래 줄이 열려 있는가"를 본다.
        var twoRows = GridSpec.WithStorage(12);
        Assert.False(Satisfied(CharmCriteriaKind.Inside, 2, 1, twoRows));

        var threeRows = GridSpec.WithStorage(18);
        Assert.True(Satisfied(CharmCriteriaKind.Inside, 2, 1, threeRows));
        Assert.False(Satisfied(CharmCriteriaKind.Inside, 0, 1, threeRows));
    }

    [Fact]
    public void BothSidesAreEmptyLooksAtTheNeighbors()
    {
        Assert.True(Satisfied(CharmCriteriaKind.BothSidesAreEmpty, 1, 0, Full));

        var occupancy = new GridOccupancy();
        occupancy.AddItem(new GridPos(0, 0), false);
        Assert.False(Satisfied(CharmCriteriaKind.BothSidesAreEmpty, 1, 0, Full, occupancy));
        Assert.False(Satisfied(CharmCriteriaKind.BothSidesAreEmpty, 0, 0, Full));
    }

    [Fact]
    public void FromTypeNameStripsThePrefixAndFallsBackToNone()
    {
        Assert.Equal(CharmCriteriaKind.SideEnd, CharmCriteria.FromTypeName("CharmActivateCriteria_SideEnd"));
        Assert.Equal(CharmCriteriaKind.FullHP, CharmCriteria.FromTypeName("FullHP"));
        Assert.Equal(CharmCriteriaKind.None, CharmCriteria.FromTypeName("CharmActivateCriteria_NotYetKnown"));
        Assert.Equal(CharmCriteriaKind.None, CharmCriteria.FromTypeName(""));
    }
}
