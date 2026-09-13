using System;
using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// F2 지정은 점수 배수가 아니라 배치 선택의 우선순위다. 표시 점수에는 섞지 않는다.
    ///
    /// 둘을 다룬다 - 지정 콤보(열쇠·종이·침처럼 자리가 카테고리를 정하는 것)와 지정한 강화
    /// 대상(침·모래시계가 누구를 강화할지)이다. 한 패스로 도는 것은 둘이 같은 아티팩트에 함께
    /// 걸릴 수 있어서다 - 침은 양쪽 모두의 대상이라, 따로 돌면 두 패스가 서로의 답을 되돌린다.
    /// </summary>
    public static class PriorityPlacement
    {
        private const int Passes = 3;

        public static bool Applies(CharmDefinition definition) =>
            ComboCounting.IsPositional(definition);

        internal static string FailureReason(PlacementProblem problem, CharmSlot charm)
        {
            var cells = Enumerable.Range(0, problem.Grid.Storage).Select(problem.Grid.ToPosition).ToList();
            if (charm.Definition.LineCategories.Count > 0)
            {
                if (!charm.Definition.LineCategories.Any(problem.PriorityCategories.Contains))
                    return "열쇠가 제공하는 콤보 중 F2에서 선택한 것이 없습니다.";
                if (!cells.Any(cell => problem.PriorityCategories.Contains(PositionalWorth.LineCategory(charm.Definition, cell))))
                    return "지정 콤보에 해당하는 행이 아직 열리지 않았습니다.";
            }
            else if (PositionalWorth.IsNeedle(charm.Definition))
            {
                if (!cells.Any(cell => problem.Grid.Contains(cell.Offset(
                        charm.Definition.DependencyOffsetX, charm.Definition.DependencyOffsetY))))
                    return "침의 방향에 대상을 놓을 칸이 없습니다.";
                if (!problem.Charms.Any(other => other != charm && !PositionalWorth.IsNeedle(other.Definition) &&
                        DirectedCharmSupport.Accepts(charm, other) && cells.Any(cell =>
                            PositionalWorth.CategoriesOf(other, cell).Any(problem.PriorityCategories.Contains))))
                    return "지정 콤보를 가진 공격 가능한 대상을 찾지 못했습니다.";
            }
            else
            {
                var centers = cells.Where(cell => problem.Grid.Contains(cell.Offset(-1, 0)) &&
                                                 problem.Grid.Contains(cell.Offset(1, 0))).ToList();
                if (centers.Count == 0) return "종이를 끼울 가로 세 칸이 없습니다.";
                if (!centers.Any(cell => problem.PriorityCategories.Any(category => problem.Charms.Count(other =>
                        other != charm && !other.IsFiller && PositionalWorth.CategoriesOf(other, cell).Contains(category)) >= 2)))
                    return "지정 콤보를 공유하는 양옆 아티팩트 둘을 찾지 못했습니다.";
            }
            return "자리·활성 조건·고정을 만족하는 지정 콤보 배치를 찾지 못했습니다.";
        }

        internal static string SupportFailureReason(PlacementProblem problem, CharmSlot charm)
        {
            var (dx, dy) = DirectedCharmSupport.Offset(charm);
            var cells = Enumerable.Range(0, problem.Grid.Storage).Select(problem.Grid.ToPosition);
            return cells.Any(cell => problem.Grid.Contains(cell.Offset(dx, dy)))
                ? "지정한 강화 대상에 닿는 배치를 찾지 못했습니다."
                : "강화 방향에 대상을 놓을 칸이 없습니다.";
        }

        public static int Compare(Arrangement left, Arrangement right)
            => PlacementQuality.From(left).CompareTo(PlacementQuality.From(right));

        internal static void Describe(
            PlacementProblem problem, Arrangement arrangement, IReadOnlyDictionary<GridPos, CharmSlot> neighbors)
        {
            if (problem.PriorityCategories.Count == 0) return;
            var categories = new List<string>();
            foreach (var charm in problem.Charms)
            {
                if (charm.IsFiller || !Applies(charm.Definition)) continue;
                categories.Clear();
                if (arrangement.CharmPositions.TryGetValue(charm.InstanceId, out var cell) &&
                    !arrangement.InactiveCharms.Contains(charm.InstanceId))
                    ComboCounting.PositionalCategories(charm, cell, neighbors, categories);

                var matched = false;
                foreach (var category in categories)
                {
                    if (!problem.PriorityCategories.Contains(category)) continue;
                    matched = true;
                    var combo = problem.Combos?.Invoke(category);
                    if (combo != null)
                        arrangement.PriorityComboProgress += problem.Scale.OfComboStep(combo,
                            ComboCounting.CountFor(problem, charm, category, neighbors), out _, out _);
                }
                if (matched) arrangement.PriorityComboMatches++;
                else arrangement.UnmatchedComboCharms.Add(charm.InstanceId);
            }
        }

        internal static Arrangement Improve(PlacementProblem problem, Arrangement best, SolverOptions options)
        {
            var cancellation = options.Cancellation;
            var designated = problem.DesignatedTargets.Count > 0;
            if (problem.PriorityCategories.Count == 0 && !designated) return best;
            cancellation.ThrowIfCancellationRequested();
            var flexible = problem.Charms.Where(charm => !charm.IsFiller &&
                    (problem.PriorityCategories.Count > 0 && Applies(charm.Definition) ||
                     designated && DirectedCharmSupport.WantsDesignatedTarget(problem, charm)))
                .OrderBy(charm => charm.InstanceId).ToList();
            if (flexible.Count == 0 || best.UnplacedTablets > 0 || best.CharmPositions.Count != problem.Charms.Count)
                return best;

            // 보통 점수의 탐색이 이미 지킨 콤보를 버렸더라도 현재 배치에서 다시 출발할 수 있다.
            if (problem.CurrentCharms.Count == problem.Charms.Count &&
                problem.Tablets.All(slot => problem.CurrentTablets.ContainsKey(slot.InstanceId)))
            {
                var layout = problem.Tablets.Select(slot =>
                {
                    var spot = problem.CurrentTablets[slot.InstanceId];
                    return slot.At(spot.Position, spot.Rotation);
                }).ToList();
                var occupied = new HashSet<GridPos>(problem.CurrentCharms.Values);
                if (occupied.Count == problem.Charms.Count && occupied.All(problem.Grid.Contains) &&
                    layout.All(tablet => problem.Grid.Contains(tablet.Position) && occupied.Add(tablet.Position)))
                {
                    var current = PlacementSolver.Score(problem, layout, problem.CurrentCharms);
                    if (Compare(current, best) > 0) best = current;
                }
            }

            var cells = Enumerable.Range(0, problem.Grid.Storage).Select(problem.Grid.ToPosition).ToList();
            if (options.PriorityComboTrials <= 0) return best;
            var remaining = options.PriorityComboTrials;
            var perItem = Math.Max(1, remaining / (Passes * flexible.Count));
            for (var pass = 0; pass < Passes; pass++)
            {
                var changed = false;
                foreach (var charm in flexible)
                {
                    if (problem.PriorityCategories.Count == 1 &&
                        !best.UnmatchedComboCharms.Contains(charm.InstanceId) &&
                        !best.UnmatchedSupportCharms.Contains(charm.InstanceId)) continue;
                    var seed = best;
                    var trials = 0;
                    foreach (var targets in Targets(problem, seed, charm, cells))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (trials++ >= perItem) break;
                        if (remaining-- <= 0) return best;
                        var trial = Place(problem, seed, targets);
                        if (Compare(trial, best) <= 0) continue;
                        best = trial;
                        changed = true;
                    }
                }
                if (!changed) break;
            }
            return best;
        }

        private static IEnumerable<(int Id, GridPos Cell)[]> Targets(
            PlacementProblem problem, Arrangement seed, CharmSlot charm, List<GridPos> cells)
        {
            var ordered = cells.OrderBy(cell => cell == seed.CharmPositions[charm.InstanceId] ? 0 : 1);
            var pending = new Queue<IEnumerator<(int Id, GridPos Cell)[]>>();
            foreach (var cell in ordered) pending.Enqueue(TargetsAt(problem, seed, charm, cell).GetEnumerator());
            try
            {
                // 한 칸의 이웃 조합이 예산을 독점하지 않게 각 중심 칸을 번갈아 탐색한다.
                while (pending.Count > 0)
                {
                    var iterator = pending.Dequeue();
                    if (!iterator.MoveNext()) { iterator.Dispose(); continue; }
                    pending.Enqueue(iterator);
                    yield return iterator.Current;
                }
            }
            finally
            {
                foreach (var iterator in pending) iterator.Dispose();
            }
        }

        private static IEnumerable<(int Id, GridPos Cell)[]> TargetsAt(
            PlacementProblem problem, Arrangement seed, CharmSlot charm, GridPos cell)
        {
            if (charm.Definition.LineCategories.Count > 0)
            {
                if (problem.PriorityCategories.Contains(PositionalWorth.LineCategory(charm.Definition, cell)))
                    yield return new[] { (charm.InstanceId, cell) };
                yield break;
            }

            if (PositionalWorth.IsNeedle(charm.Definition))
            {
                var targetCell = cell.Offset(charm.Definition.DependencyOffsetX, charm.Definition.DependencyOffsetY);
                if (targetCell == cell || !problem.Grid.Contains(targetCell)) yield break;
                var neighbors = problem.Charms.ToDictionary(other => seed.CharmPositions[other.InstanceId]);
                foreach (var target in problem.Charms.OrderBy(other => seed.CharmPositions[other.InstanceId] == targetCell ? 0 : 1))
                {
                    if (!DirectedCharmSupport.Accepts(charm, target) ||
                        !target.IsSupportTarget &&
                        !PositionalWorth.CategoriesOf(target, targetCell, neighbors).Any(problem.PriorityCategories.Contains)) continue;
                    yield return new[] { (charm.InstanceId, cell), (target.InstanceId, targetCell) };
                }
                yield break;
            }

            // 모래시계·별조각은 카테고리를 물려받지 않으므로 지정한 대상일 때만 옮겨 볼 것이 있다.
            if (charm.Definition.MagicSupport is { } support)
            {
                var magicCell = cell.Offset(support.OffsetX, support.OffsetY);
                if (magicCell == cell || !problem.Grid.Contains(magicCell)) yield break;
                foreach (var target in problem.Charms.OrderBy(other => seed.CharmPositions[other.InstanceId] == magicCell ? 0 : 1))
                {
                    if (!target.IsSupportTarget || !DirectedCharmSupport.Accepts(charm, target)) continue;
                    yield return new[] { (charm.InstanceId, cell), (target.InstanceId, magicCell) };
                }
                yield break;
            }

            var left = cell.Offset(-1, 0);
            var right = cell.Offset(1, 0);
            if (!problem.Grid.Contains(left) || !problem.Grid.Contains(right)) yield break;
            foreach (var category in problem.PriorityCategories.OrderBy(value => value, StringComparer.Ordinal))
            {
                var candidates = problem.Charms.Where(other => other != charm && !other.IsFiller &&
                    PositionalWorth.CategoriesOf(other, cell).Contains(category)).ToList();
                foreach (var a in candidates.OrderBy(other => seed.CharmPositions[other.InstanceId] == left ? 0 : 1))
                {
                    foreach (var b in candidates.OrderBy(other => seed.CharmPositions[other.InstanceId] == right ? 0 : 1))
                    {
                        if (a == b) continue;
                        yield return new[] { (charm.InstanceId, cell), (a.InstanceId, left), (b.InstanceId, right) };
                    }
                }
            }
        }

        private static Arrangement Place(
            PlacementProblem problem, Arrangement seed, (int Id, GridPos Cell)[] targets)
        {
            var positions = new Dictionary<int, GridPos>(seed.CharmPositions);
            var layout = seed.Tablets.Select((tablet, index) =>
                problem.Tablets[index].At(tablet.Position, tablet.Rotation)).ToList();
            foreach (var target in targets)
            {
                var from = positions[target.Id];
                if (from == target.Cell) continue;
                foreach (var pair in positions)
                {
                    if (pair.Value != target.Cell) continue;
                    positions[pair.Key] = from;
                    break;
                }
                // 석판이 가로막은 세 칸도 교환으로 비운다. 석판 인스턴스 순서와 회전은 보존한다.
                for (var index = 0; index < layout.Count; index++)
                {
                    if (layout[index].Position != target.Cell) continue;
                    layout[index] = problem.Tablets[index].At(from, layout[index].Rotation);
                    break;
                }
                positions[target.Id] = target.Cell;
            }
            return PlacementSolver.Score(problem, layout, positions);
        }
    }
}
