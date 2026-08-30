using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    public sealed class SimulationResult
    {
        public Dictionary<GridPos, int> Level { get; } = new Dictionary<GridPos, int>();
        public Dictionary<GridPos, int> Disable { get; } = new Dictionary<GridPos, int>();
        public Dictionary<GridPos, int> IgnoreCriteria { get; } = new Dictionary<GridPos, int>();
        public Dictionary<GridPos, int> MultiplyLevel { get; } = new Dictionary<GridPos, int>();

        /// <summary>입력 순서에 대응하는 적용 여부. 조건을 만족하지 못한 석판은 효과가 없다.</summary>
        public bool[] Applied { get; internal set; } = new bool[0];

        public int LevelAt(GridPos position) => Level.TryGetValue(position, out var value) ? value : 0;
        public bool IsDisabled(GridPos position) => Disable.TryGetValue(position, out var value) && value > 0;
    }

    /// <summary>
    /// 석판 배치가 만들어내는 행렬을 계산한다. 게임 <c>StoneTablet.ApplyEffect</c>의 포팅이다.
    ///
    /// 석판끼리는 서로의 조건 판정에 영향을 주지 않는다. 조건은 아이템 배치만 보기 때문이다.
    /// 덕분에 순서에 무관하고, 최적화 탐색에서 부분 결과를 재사용할 수 있다.
    /// </summary>
    public static class TabletSimulator
    {
        public static SimulationResult Run(
            IReadOnlyList<TabletPlacement> placements, GridOccupancy occupancy, GridSpec grid)
        {
            var result = new SimulationResult { Applied = new bool[placements.Count] };

            for (var i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                if (!MeetsCriteria(placement, occupancy, grid)) continue;

                result.Applied[i] = true;
                ApplyEffects(placement, grid, result);
            }
            return result;
        }

        public static bool MeetsCriteria(TabletPlacement placement, GridOccupancy occupancy, GridSpec grid)
        {
            var cells = TabletQuery.Parse(placement.ConditionQuery, grid, placement.Position, placement.Rotation);
            if (cells.Count == 0) return true;

            var allHit = true;
            var sawPlaced = false;
            var anyPlaced = false;

            foreach (var cell in cells)
            {
                bool hit;
                bool placed;
                switch (QueryValue.ReadCriteria(cell.Value))
                {
                    case TabletCriteriaKind.AnyItem:
                        hit = occupancy.HasItem(cell.Position);
                        placed = false;
                        break;
                    case TabletCriteriaKind.OnlyCharm:
                        hit = occupancy.HasCharm(cell.Position);
                        placed = false;
                        break;
                    case TabletCriteriaKind.Placed:
                        hit = true;
                        sawPlaced = true;
                        placed = cell.Position == placement.Position;
                        break;
                    default:
                        hit = true;
                        placed = true;
                        break;
                }
                allHit &= hit;
                anyPlaced |= placed;
            }

            return allHit && (anyPlaced || !sawPlaced);
        }

        private static void ApplyEffects(TabletPlacement placement, GridSpec grid, SimulationResult result)
        {
            foreach (var cell in TabletQuery.Parse(placement.Query, grid, placement.Position, placement.Rotation))
            {
                var (kind, levelParam) = QueryValue.ReadEffect(cell.Value);
                switch (kind)
                {
                    case TabletEffectKind.IncreaseConstLevel:
                        Accumulate(result.Level, cell.Position, levelParam);
                        break;
                    case TabletEffectKind.Disable:
                        Accumulate(result.Disable, cell.Position, 1);
                        break;
                    case TabletEffectKind.IgnoreCriteria:
                        Accumulate(result.IgnoreCriteria, cell.Position, 1);
                        break;
                    case TabletEffectKind.MultiplyConstLevel:
                        Accumulate(result.MultiplyLevel, cell.Position, levelParam);
                        break;
                }
            }
        }

        private static void Accumulate(Dictionary<GridPos, int> matrix, GridPos position, int amount)
        {
            matrix.TryGetValue(position, out var current);
            matrix[position] = current + amount;
        }
    }
}
