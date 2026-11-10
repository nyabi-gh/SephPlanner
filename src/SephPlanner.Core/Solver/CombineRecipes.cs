using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 합치기형 배치 보너스. 게임 1.1.2 <c>GridInventory.SearchArrangementBonusInInventory</c> 는 레시피의 재료가
    /// 상하좌우로 이어진 한 덩어리를 이루면 다음 서버 <c>LateUpdate</c> 에 재료를 지우고 결과 아이템을 넣는다.
    /// 되돌릴 수 없으므로 배치 추천은 그런 덩어리를 만들지 않는다(2026-11-10 사용자 결정).
    /// </summary>
    internal static class CombineRecipes
    {
        private static readonly int[] StepX = { 1, -1, 0, 0 };
        private static readonly int[] StepY = { 0, 0, 1, -1 };

        /// <summary>이 배치에서 합쳐질 덩어리 수. 게임처럼 같은 칸 묶음은 한 번만 센다.</summary>
        internal static int Count(PlacementProblem problem, IReadOnlyDictionary<int, GridPos> positions) =>
            Groups(problem, positions).Count;

        /// <summary>합쳐질 덩어리마다 그 재료의 인스턴스 번호.</summary>
        internal static List<List<int>> Groups(PlacementProblem problem, IReadOnlyDictionary<int, GridPos> positions)
        {
            var groups = new List<List<int>>();
            if (problem.CombineRecipes.Count == 0) return groups;

            var materials = new HashSet<int>(problem.CombineRecipes.SelectMany(recipe => recipe));
            var cells = new Dictionary<GridPos, CharmSlot>();
            foreach (var charm in problem.Charms)
            {
                if (!materials.Contains(charm.Definition.EntityId)) continue;
                if (positions.TryGetValue(charm.InstanceId, out var cell) && problem.Grid.Contains(cell)) cells[cell] = charm;
            }
            if (cells.Count < 2) return groups;

            var seen = new HashSet<string>();
            foreach (var recipe in problem.CombineRecipes)
            {
                if (recipe.Count == 0 || cells.Count < recipe.Count) continue;
                var expected = new Dictionary<int, int>();
                foreach (var entity in recipe) expected[entity] = expected.TryGetValue(entity, out var n) ? n + 1 : 1;

                foreach (var start in cells)
                {
                    if (!expected.ContainsKey(start.Value.Definition.EntityId)) continue;
                    var remaining = new Dictionary<int, int>(expected);
                    Take(remaining, start.Value.Definition.EntityId);
                    var group = new List<GridPos> { start.Key };
                    Expand(problem, cells, group, remaining, groups, seen);
                }
            }
            return groups;
        }

        private static void Expand(
            PlacementProblem problem, Dictionary<GridPos, CharmSlot> cells, List<GridPos> group,
            Dictionary<int, int> remaining, List<List<int>> groups, HashSet<string> seen)
        {
            if (remaining.Count == 0)
            {
                var key = string.Join(";", group.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).Select(cell => cell.X + "," + cell.Y));
                if (seen.Add(key)) groups.Add(group.Select(cell => cells[cell].InstanceId).ToList());
                return;
            }
            for (var index = 0; index < group.Count; index++)
            {
                var from = group[index];
                for (var step = 0; step < StepX.Length; step++)
                {
                    var next = new GridPos(from.X + StepX[step], from.Y + StepY[step]);
                    if (group.Contains(next) || !cells.TryGetValue(next, out var charm)) continue;
                    var entity = charm.Definition.EntityId;
                    if (!remaining.ContainsKey(entity)) continue;

                    Take(remaining, entity);
                    group.Add(next);
                    Expand(problem, cells, group, remaining, groups, seen);
                    group.RemoveAt(group.Count - 1);
                    remaining[entity] = remaining.TryGetValue(entity, out var left) ? left + 1 : 1;
                }
            }
        }

        private static void Take(Dictionary<int, int> remaining, int entity)
        {
            var count = remaining[entity];
            if (count <= 1) remaining.Remove(entity);
            else remaining[entity] = count - 1;
        }
    }
}
