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

        /// <summary>
        /// 옮길 것을 하나씩 제자리에 넣는 순서 가운데, <b>도중의 어느 배치에서도</b> 새 덩어리가 생기지 않는 것.
        /// 끝 배치만 떼어 두어서는 모자라다 - 참가자의 자동 배치는 교환마다 서버 왕복을 기다리고 손으로 옮길 때도
        /// 한 수씩 반영되므로, 그 사이 서버 <c>LateUpdate</c> 가 중간 배치의 덩어리를 합쳐 버린다.
        ///
        /// 한 걸음은 게임의 칸 교환이다(<see cref="Planning.MoveOrder"/>, <c>ApplyPlanRoutine.ApplySwaps</c> 와 같은 걸음).
        /// 받은 순서가 안전하면 그대로 돌려주고, 찾지 못하면 null 이다. 지금 배치에 이미 있는 덩어리는 게임이 합치지
        /// 않고 둔 것이라 세지 않는다.
        /// </summary>
        internal static List<int>? SafeOrder(
            PlacementProblem problem, IReadOnlyDictionary<int, GridPos> current, IReadOnlyList<(int Id, GridPos To)> movers)
        {
            var order = new List<int>(movers.Count);
            if (problem.CombineRecipes.Count == 0)
            {
                foreach (var mover in movers) order.Add(mover.Id);
                return order;
            }

            var positions = new Dictionary<int, GridPos>();
            var occupants = new Dictionary<GridPos, int>();
            foreach (var pair in current)
            {
                positions[pair.Key] = pair.Value;
                occupants[pair.Value] = pair.Key;
            }
            var standing = new HashSet<string>();
            foreach (var group in Groups(problem, positions)) standing.Add(Key(group));

            var budget = SafeOrderBudget;
            return Place(problem, positions, occupants, standing, new List<(int Id, GridPos To)>(movers), order, ref budget)
                ? order
                : null;
        }

        /// <summary>
        /// 순서 탐색이 펼쳐 볼 걸음 수의 상한. 받은 순서가 안전하면 옮길 것의 수만큼만 쓴다. 넘기면 못 찾은 것으로
        /// 보고 적용을 막는다 - 잰 값이 아니라 조언 시간에 드러나지 않을 만큼으로 고른 추정치다.
        /// </summary>
        private const int SafeOrderBudget = 20000;

        private static bool Place(
            PlacementProblem problem, Dictionary<int, GridPos> positions, Dictionary<GridPos, int> occupants,
            HashSet<string> standing, List<(int Id, GridPos To)> remaining, List<int> order, ref int budget)
        {
            if (remaining.Count == 0) return true;

            for (var index = 0; index < remaining.Count; index++)
            {
                if (budget-- <= 0) return false;

                var mover = remaining[index];
                if (!positions.TryGetValue(mover.Id, out var from)) return false;
                var displaced = from != mover.To && occupants.TryGetValue(mover.To, out var occupant) ? occupant : 0;
                Swap(positions, occupants, mover.Id, from, mover.To, displaced);

                if (from == mover.To || !CreatesGroup(problem, positions, standing))
                {
                    remaining.RemoveAt(index);
                    order.Add(mover.Id);
                    if (Place(problem, positions, occupants, standing, remaining, order, ref budget)) return true;
                    order.RemoveAt(order.Count - 1);
                    remaining.Insert(index, mover);
                }
                Swap(positions, occupants, mover.Id, mover.To, from, displaced);
            }
            return false;
        }

        private static void Swap(
            Dictionary<int, GridPos> positions, Dictionary<GridPos, int> occupants, int id, GridPos from, GridPos to, int displaced)
        {
            if (from == to) return;
            positions[id] = to;
            occupants[to] = id;
            if (displaced == 0) occupants.Remove(from);
            else
            {
                positions[displaced] = from;
                occupants[from] = displaced;
            }
        }

        private static bool CreatesGroup(PlacementProblem problem, Dictionary<int, GridPos> positions, HashSet<string> standing)
        {
            foreach (var group in Groups(problem, positions))
                if (!standing.Contains(Key(group))) return true;
            return false;
        }

        private static string Key(List<int> group) => string.Join(",", group.OrderBy(id => id));

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
