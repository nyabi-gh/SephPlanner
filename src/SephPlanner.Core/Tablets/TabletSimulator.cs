using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 격자 크기의 평면 배열로 행렬을 담는다. 빔 탐색이 후보마다 시뮬레이션을 새로 돌려 여기가
    /// 풀이 시간의 대부분이었는데, 딕셔너리를 배열로 바꾸는 것만으로 크게 줄었다. 격자 밖 좌표는
    /// 게임 행렬에도 반영되지 않으므로 쓰기는 버리고 읽기는 0 으로 답한다.
    /// </summary>
    public sealed class SimulationResult
    {
        private readonly GridSpec _grid;
        private readonly int[] _level;
        private readonly int[] _disable;
        private readonly int[] _ignore;
        private readonly int[] _multiply;

        public SimulationResult(GridSpec grid, int placements)
        {
            _grid = grid;
            var size = grid.Width * grid.Height;
            _level = new int[size];
            _disable = new int[size];
            _ignore = new int[size];
            _multiply = new int[size];
            Applied = new bool[placements];
        }

        /// <summary>입력 순서에 대응하는 적용 여부. 조건을 만족하지 못한 석판은 효과가 없다.</summary>
        public bool[] Applied { get; }

        private bool Contains(GridPos position) =>
            position.X >= 0 && position.X < _grid.Width &&
            position.Y >= 0 && position.Y < _grid.Height;

        public int LevelAt(GridPos position) =>
            Contains(position) ? _level[_grid.ToIndex(position.X, position.Y)] : 0;

        public bool IsDisabled(GridPos position) =>
            Contains(position) && _disable[_grid.ToIndex(position.X, position.Y)] > 0;

        public int IgnoreCriteriaAt(GridPos position) =>
            Contains(position) ? _ignore[_grid.ToIndex(position.X, position.Y)] : 0;

        public int MultiplierAt(GridPos position) =>
            Contains(position) ? _multiply[_grid.ToIndex(position.X, position.Y)] : 0;

        /// <summary>
        /// 그 칸에 놓인 아티팩트가 받는 레벨. 게임과 같은 순서로 석판 몫과 인챈트를 먼저 더하고
        /// 배수를 마지막에 곱한다(<c>GridInventory.ReleasePermission</c>). 순서를 바꾸면 값이 달라진다.
        /// </summary>
        public int EffectiveLevel(GridPos position, int enchant)
        {
            var level = LevelAt(position) + enchant;

            // 배수는 게임과 같이 덧셈으로 쌓인다(MUL/2 + MUL/3 = x5). 합이 0이면 게임의
            // ReleasePermission 도 곱셈을 건너뛰므로(x0 이 아니라 x1) 같은 가드를 둔다.
            if (Contains(position))
            {
                var multiplier = _multiply[_grid.ToIndex(position.X, position.Y)];
                if (multiplier != 0) level *= multiplier;
            }
            return level;
        }

        internal void AddLevel(GridPos position, int amount) => Add(_level, position, amount);
        internal void AddDisable(GridPos position, int amount) => Add(_disable, position, amount);
        internal void AddIgnoreCriteria(GridPos position, int amount) => Add(_ignore, position, amount);
        internal void AddMultiply(GridPos position, int amount) => Add(_multiply, position, amount);

        private void Add(int[] matrix, GridPos position, int amount)
        {
            if (Contains(position)) matrix[_grid.ToIndex(position.X, position.Y)] += amount;
        }
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
            IReadOnlyList<TabletPlacement> placements, GridOccupancy occupancy, GridSpec grid,
            IReadOnlyList<FixedEffectCell>? fixedEffects = null)
        {
            var result = new SimulationResult(grid, placements.Count);

            // 고정 각인 몫. 게임은 석판보다 먼저 더하지만(ReleasePermission) 덧셈이라 순서는 무관하고,
            // 배수도 같은 행렬에 쌓인다.
            foreach (var cell in fixedEffects ?? System.Array.Empty<FixedEffectCell>())
            {
                if (cell.Level != 0) result.AddLevel(cell.Position, cell.Level);
                if (cell.Disable != 0) result.AddDisable(cell.Position, cell.Disable);
                if (cell.IgnoreCriteria != 0) result.AddIgnoreCriteria(cell.Position, cell.IgnoreCriteria);
                if (cell.Multiply != 0) result.AddMultiply(cell.Position, cell.Multiply);
            }

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
                        // 해석되지 않는 값은 게임도 조건을 만족한 것으로 친다
                        // (StoneTablet.ApplyEffect 의 default: flag4 = true, flag5 = true).
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
                        result.AddLevel(cell.Position, levelParam);
                        break;
                    case TabletEffectKind.Disable:
                        result.AddDisable(cell.Position, 1);
                        break;
                    case TabletEffectKind.IgnoreCriteria:
                        result.AddIgnoreCriteria(cell.Position, 1);
                        break;
                    case TabletEffectKind.MultiplyConstLevel:
                        result.AddMultiply(cell.Position, levelParam);
                        break;
                }
            }
        }
    }
}
