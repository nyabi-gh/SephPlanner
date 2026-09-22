using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 살아 있는 가방에서 시뮬레이션에 필요한 것만 한 번에 읽는다. 읽기와 검증이 같은 격자·같은
    /// 석판 목록을 보게 하려고 한 곳에 모았다.
    /// </summary>
    internal sealed class InventoryView
    {
        public GridSpec Grid { get; private set; }
        public GridOccupancy Occupancy { get; private set; } = new GridOccupancy();
        public List<TabletPlacement> Placements { get; } = new List<TabletPlacement>();
        public List<StoneTablet> Tablets { get; } = new List<StoneTablet>();
        public Dictionary<GridPos, int> Enchants { get; } = new Dictionary<GridPos, int>();

        /// <summary>참조가 끊겨 읽지 못한 석판·각인 수.</summary>
        public int Severed { get; private set; }

        public string Sources { get; private set; } = "";
        public string Arrangement { get; private set; } = "";

        public static InventoryView Of(GridInventory inv)
        {
            var view = new InventoryView
            {
                Grid = GameReader.GridOf(inv),
            };

            var occupied = new List<string>();
            foreach (var pair in inv.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null) continue;

                // 조건 판정의 AnyItem 은 석판이 놓인 칸도 아이템으로 센다.
                var isCharm = instance.Entity != null && instance.Entity.type == EItemType.Charm;
                var position = new GridPos(pair.Key.x, pair.Key.y);
                view.Occupancy.AddItem(position, isCharm);
                occupied.Add($"{pair.Key.x},{pair.Key.y}:{(isCharm ? 'c' : 'i')}");

                if (instance.StoneTablet == null)
                    view.Enchants[position] = GameReader.EnchantOf(instance.InstanceID);
            }

            var seen = new HashSet<int>();
            var sources = new List<string>();
            foreach (var pair in inv.stoneTablets) view.Add(pair.Value, seen, sources);
            foreach (var engraving in inv.engravings) view.Add(engraving, seen, sources);

            occupied.Sort(System.StringComparer.Ordinal);
            sources.Sort(System.StringComparer.Ordinal);
            view.Arrangement = string.Join("|", occupied.ToArray());
            view.Sources = string.Join("|", sources.ToArray());
            return view;
        }

        public GameEffectMatrices Matrices(GridInventory inv) => new GameEffectMatrices(
            position => Lookup(inv.levelMatrix, position),
            position => Lookup(inv.multiplyLevelMatrix, position),
            position => Lookup(inv.disableMatrix, position),
            position => Lookup(inv.ignoreCriteriaMatrix, position),
            position => Enchants.TryGetValue(position, out var enchant) ? enchant : 0);

        private static int Lookup(Mirror.SyncDictionary<ItemPosition, int> matrix, GridPos position)
        {
            matrix.TryGetValue(new ItemPosition((sbyte)position.X, (sbyte)position.Y), out var value);
            return value;
        }

        private void Add(StoneTablet tablet, HashSet<int> seen, List<string> sources)
        {
            if (tablet == null)
            {
                Severed++;
                return;
            }
            if (!seen.Add(tablet.instanceID)) return;

            Tablets.Add(tablet);
            Placements.Add(new TabletPlacement
            {
                Definition = new TabletDefinition(),
                Position = new GridPos(tablet.xIdx, tablet.yIdx),
                Rotation = tablet.rotation,
                InstanceQuery = tablet.GetQuery(tablet.instanceID) ?? "",
                InstanceConditionQuery = tablet.GetConditionQuery(tablet.instanceID) ?? "",
            });
            sources.Add($"{tablet.instanceID}@{tablet.xIdx},{tablet.yIdx},{tablet.rotation}");
        }
    }
}
