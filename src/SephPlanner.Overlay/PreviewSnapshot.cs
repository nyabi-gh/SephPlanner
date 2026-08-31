using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;

namespace SephPlanner.Overlay;

/// <summary>
/// `--preview`로 실행했을 때 쓰는 가짜 스냅샷. 게임을 켜지 않고 화면을 확인하기 위한 것이라
/// 실제 덤프(inventory-dump.txt)에서 본 격자 크기와 엔티티 ID를 그대로 흉내 낸다.
/// </summary>
public static class PreviewSnapshot
{
    /// <summary>
    /// 미리보기 전용 카탈로그. 진짜 덤프에 기대면 게임을 한 번도 안 돌린 PC에서는 미리보기가
    /// 안내문만 띄우고 끝난다. 이름과 수치는 전부 지어낸 것이다 - 게임 데이터를 코드에 넣으면
    /// 저장소에 게임 저작물이 들어가는 셈이 된다 (docs/LEGAL.md).
    /// </summary>
    public static ICatalog Catalog() => new Catalog(
        new[]
        {
            new TabletDefinition { EntityId = 2025, Id = "PreviewA", IsRotatable = true, Query = "RIGHT 2\nDOWN 1", Names = { ["current"] = "미리보기 석판 A" } },
            new TabletDefinition { EntityId = 2044, Id = "PreviewB", Query = "LEFT -1\nRIGHT 3", Names = { ["current"] = "미리보기 석판 B" } },
            new TabletDefinition { EntityId = 12000, Id = "PreviewC", IsRotatable = true, Query = "HORIZONTAL 1", Rarity = Rarity.Legend, Names = { ["current"] = "미리보기 석판 C" } },
            new TabletDefinition { EntityId = 2001, Id = "PreviewD", Query = "DOWN 2", Names = { ["current"] = "미리보기 석판 D" } },
            // 합성 석판. 정의는 껍데기라 질의도 이름도 인스턴스가 들고 있다.
            new TabletDefinition { EntityId = 2101, Id = "...", Names = { ["current"] = "..." } },
        },
        new[]
        {
            new CharmDefinition
            {
                EntityId = 1237, Id = "PreviewCharmA", MaxLevel = 3, Rarity = Rarity.Rare,
                Categories = { "EMBER" }, Names = { ["current"] = "미리보기 아티팩트 A" },
                EffectLines = { "적을 맞힐 때마다 미리보기 피해를 1~3 줍니다.", "미리보기 효과가 2초간 남습니다." },
            },
            new CharmDefinition { EntityId = 3002, Id = "PreviewCharmB", MaxLevel = 4, Categories = { "FLAMESWORD" }, Names = { ["current"] = "미리보기 아티팩트 B" } },
            new CharmDefinition { EntityId = 3012, Id = "PreviewCharmC", MaxLevel = 3, Categories = { "EMBER" }, Names = { ["current"] = "미리보기 아티팩트 C" } },
        },
        new[]
        {
            new ComboDefinition
            {
                Id = "EMBER", Thresholds = { 2, 5, 8 }, Names = { ["current"] = "미리보기 콤보 A" },
                Effects =
                {
                    new ComboEffectLine { Threshold = 2, Text = "미리보기 효과 첫 단계" },
                    new ComboEffectLine { Threshold = 5, Text = "미리보기 효과 둘째 단계" },
                },
            },
            new ComboDefinition
            {
                Id = "FLAMESWORD", Thresholds = { 3, 6 }, Names = { ["current"] = "미리보기 콤보 B" },
                Effects = { new ComboEffectLine { Threshold = 3, Text = "미리보기 효과" } },
            },
        });

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
                // 석판 효과에서 먼 자리에 두어 이동 제안과 자동 배치 버튼까지 미리보기에 나오게 한다.
                new PlacedItem { DefinitionId = 1237, InstanceId = 1, Position = new GridPos(5, 3), IsActive = true },
                new PlacedItem { DefinitionId = 3002, InstanceId = 2, Position = new GridPos(2, 0), IsActive = true },
                new PlacedItem { DefinitionId = 3012, InstanceId = 3, Position = new GridPos(0, 2), IsActive = true },
                new PlacedItem { DefinitionId = 0, InstanceId = 4, Position = new GridPos(1, 0) },
            },
            Tablets =
            {
                new PlacedTablet { DefinitionId = 2025, InstanceId = 11, Position = new GridPos(1, 1), Rotation = 1, IsApplied = true },
                new PlacedTablet { DefinitionId = 2044, InstanceId = 12, Position = new GridPos(0, 1), IsApplied = true },
                new PlacedTablet
                {
                    DefinitionId = 2101, InstanceId = 13, Position = new GridPos(4, 1), IsApplied = true,
                    Query = "UP 2\nDOWN 1", IsRotatable = true, Name = "미리보기 합성 석판",
                },
            },
            ComboCounts = { ["EMBER"] = 5, ["FLAMESWORD"] = 3 },
        },
        Offers =
        {
            new OfferedItem { DefinitionId = 12000, Kind = "tablet", Price = 400, SlotIndex = 0 },
            new OfferedItem { DefinitionId = 2001, Kind = "tablet", Price = 120, SlotIndex = 1 },
            new OfferedItem { DefinitionId = 1237, Kind = "charm", Price = 0, SlotIndex = 2 },
        },
    };
}
