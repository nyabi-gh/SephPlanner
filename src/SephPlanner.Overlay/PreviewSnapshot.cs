using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;

namespace SephPlanner.Overlay;

/// <summary>
/// `--preview`로 실행했을 때 쓰는 가짜 스냅샷. 게임을 켜지 않고 화면을 확인하기 위한 것이라
/// 실제 덤프(inventory-dump.txt)에서 본 격자 크기와 엔티티 ID를 그대로 흉내 낸다.
/// </summary>
public static class PreviewSnapshot
{
    public static GameSnapshot Build() => new()
    {
        Run = new RunState { WeaponId = "", Gold = 260 },
        Inventory = new InventoryState
        {
            Width = 6,
            Height = 4,
            Storage = 24,
            Items =
            {
                new PlacedItem { DefinitionId = 1237, InstanceId = 1, Position = new GridPos(0, 0), IsActive = true },
                new PlacedItem { DefinitionId = 3002, InstanceId = 2, Position = new GridPos(2, 0), IsActive = true },
                new PlacedItem { DefinitionId = 3012, InstanceId = 3, Position = new GridPos(0, 2), IsActive = true },
                new PlacedItem { DefinitionId = 0, InstanceId = 4, Position = new GridPos(1, 0) },
            },
            Tablets =
            {
                new PlacedTablet { DefinitionId = 2025, InstanceId = 11, Position = new GridPos(1, 1), Rotation = 1, IsApplied = true },
                new PlacedTablet { DefinitionId = 2044, InstanceId = 12, Position = new GridPos(0, 1), IsApplied = true },
            },
        },
        Offers =
        {
            new OfferedItem { DefinitionId = 12000, Kind = "tablet", Price = 400, SlotIndex = 0 },
            new OfferedItem { DefinitionId = 2001, Kind = "tablet", Price = 120, SlotIndex = 1 },
            new OfferedItem { DefinitionId = 1237, Kind = "charm", Price = 0, SlotIndex = 2 },
        },
    };
}
