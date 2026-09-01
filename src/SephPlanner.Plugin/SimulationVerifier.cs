using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Plugin
{
    internal sealed class RuntimeSimulationCheck
    {
        public PlanVerificationStatus Status { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// Core 의 석판 시뮬레이터를 게임의 실제 적용 결과와 대조한다.
    /// 게임이 이미 계산해 둔 <c>IsApplied</c>와 <c>EffectRange</c>, 그리고 <c>levelMatrix</c>가 정답지다.
    ///
    /// 석판 질의만 맞아도 최종 레벨은 어긋날 수 있다. 각인과 세트 효과, 배치 보너스가 레벨에
    /// 더해지는데 우리는 그것들을 아직 모델에 넣지 않았기 때문이다. 그래서 마지막 레벨까지 대조한다.
    /// </summary>
    internal static class SimulationVerifier
    {
        /// <summary>마지막 대조에서 확인한 석판 수. 검증이 실제로 돌았는지 판단하는 근거다.</summary>
        public static int LastCheckedTablets { get; private set; }

        /// <summary>일치하면 null, 어긋나면 첫 번째 차이를 설명하는 문자열.</summary>
        public static string Check(GridInventory inv)
        {
            var grid = new GridSpec(inv.Width, inv.Height, inv.CurrentInventoryStorage);
            var occupancy = BuildOccupancy(inv);

            var placements = new List<TabletPlacement>();
            var tablets = new List<StoneTablet>();
            var seenTablets = new HashSet<int>();
            foreach (var tablet in inv.stoneTablets.Values)
            {
                AddTablet(tablet, seenTablets, tablets, placements);
            }
            foreach (var tablet in inv.engravings)
                AddTablet(tablet, seenTablets, tablets, placements);
            LastCheckedTablets = placements.Count;

            // 고정 각인 몫까지 넣어야 게임 levelMatrix 와 같은 기준으로 견주게 된다.
            var result = TabletSimulator.Run(placements, occupancy, grid, GameReader.ReadFixedEffects(inv));

            for (var i = 0; i < tablets.Count; i++)
            {
                if (result.Applied[i] != tablets[i].IsApplied)
                    return $"석판 {tablets[i].entityID} 적용 여부 {result.Applied[i]} != {tablets[i].IsApplied}";

                if (!result.Applied[i]) continue;

                var difference = CompareEffects(placements[i], grid, tablets[i]);
                if (difference != null) return $"석판 {tablets[i].entityID} {difference}";
            }

            return CompareMatrices(inv, grid, result);
        }

        private static void AddTablet(
            StoneTablet tablet, HashSet<int> seen,
            List<StoneTablet> tablets, List<TabletPlacement> placements)
        {
            if (tablet == null || !seen.Add(tablet.instanceID)) return;
            tablets.Add(tablet);
            placements.Add(new TabletPlacement
            {
                Definition = new TabletDefinition(),
                Position = new GridPos(tablet.xIdx, tablet.yIdx),
                Rotation = tablet.rotation,
                InstanceQuery = tablet.GetQuery(tablet.instanceID) ?? "",
                InstanceConditionQuery = tablet.GetConditionQuery(tablet.instanceID) ?? "",
            });
        }

        private static string CompareMatrices(GridInventory inv, GridSpec grid, SimulationResult result)
        {
            var enchants = new Dictionary<GridPos, int>();
            foreach (var pair in inv.inventoryMatrix)
            {
                var item = pair.Value;
                if (item == null || item.StoneTablet != null) continue;
                var position = new GridPos(pair.Key.x, pair.Key.y);
                enchants[position] = GameReader.EnchantOf(item.InstanceID);
            }

            for (var index = 0; index < grid.Storage; index++)
            {
                var position = grid.ToPosition(index);
                enchants.TryGetValue(position, out var enchant);
                var ours = result.EffectiveLevel(position, enchant);
                var theirs = LookupLevel(inv, (sbyte)position.X, (sbyte)position.Y);
                if (ours != theirs)
                    return $"칸 {position} 레벨 {ours} != 게임 {theirs} (인챈트 {enchant})";

                var disabled = LookupDisabled(inv, (sbyte)position.X, (sbyte)position.Y);
                if (result.IsDisabled(position) != disabled)
                    return $"칸 {position} 비활성 {result.IsDisabled(position)} != 게임 {disabled}";
            }
            return null;
        }

        private static int LookupLevel(GridInventory inv, sbyte x, sbyte y)
        {
            foreach (var pair in inv.levelMatrix)
                if (pair.Key.x == x && pair.Key.y == y) return pair.Value;
            return 0;
        }

        private static bool LookupDisabled(GridInventory inv, sbyte x, sbyte y)
        {
            foreach (var pair in inv.disableMatrix)
                if (pair.Key.x == x && pair.Key.y == y) return pair.Value > 0;
            return false;
        }

        private static GridOccupancy BuildOccupancy(GridInventory inv)
        {
            // 조건 판정의 AnyItem 은 석판이 놓인 칸도 아이템으로 센다.
            var occupancy = new GridOccupancy();
            foreach (var pair in inv.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null) continue;
                var isCharm = instance.Entity != null && instance.Entity.type == EItemType.Charm;
                occupancy.AddItem(new GridPos(pair.Key.x, pair.Key.y), isCharm);
            }
            return occupancy;
        }

        private static string CompareEffects(TabletPlacement placement, GridSpec grid, StoneTablet tablet)
        {
            var cells = TabletQuery.Parse(placement.Query, grid, placement.Position, placement.Rotation);
            if (cells.Count != tablet.EffectRange.Count)
                return $"효과 칸 수 {cells.Count} != {tablet.EffectRange.Count}";

            for (var i = 0; i < cells.Count; i++)
            {
                var expected = tablet.EffectRange[i];
                var (kind, levelParam) = QueryValue.ReadEffect(cells[i].Value);

                if (cells[i].Position.X != expected.position.x || cells[i].Position.Y != expected.position.y)
                    return $"효과 [{i}] 위치 불일치";
                if ((int)kind != (int)expected.effectType)
                    return $"효과 [{i}] 종류 {kind} != {expected.effectType}";
                if (levelParam != expected.levelParam)
                    return $"효과 [{i}] 수치 {levelParam} != {expected.levelParam}";
            }
            return null;
        }
    }
}
