using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// Core 의 석판 시뮬레이터를 게임의 실제 적용 결과와 대조한다.
    /// 게임이 이미 계산해 둔 <c>IsApplied</c>와 <c>EffectRange</c>가 정답지다.
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
            foreach (var tablet in inv.stoneTablets.Values)
            {
                if (tablet == null) continue;
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
            LastCheckedTablets = placements.Count;
            if (placements.Count == 0) return null;

            var result = TabletSimulator.Run(placements, occupancy, grid);

            for (var i = 0; i < tablets.Count; i++)
            {
                if (result.Applied[i] != tablets[i].IsApplied)
                    return $"석판 {tablets[i].entityID} 적용 여부 {result.Applied[i]} != {tablets[i].IsApplied}";

                if (!result.Applied[i]) continue;

                var difference = CompareEffects(placements[i], grid, tablets[i]);
                if (difference != null) return $"석판 {tablets[i].entityID} {difference}";
            }
            return null;
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
