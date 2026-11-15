using System;
using System.Collections.Generic;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 이 배치에서 아티팩트가 켜지는지. 1.1.0 나침반은 꺼지면 카테고리도 피해 보너스도 없고 사슬도
    /// 끊으므로, 나침반을 따라가는 걸음이 이것을 묻는다.
    ///
    /// <b>시뮬레이션 없이 콤보 수만 세는 자리(<c>default</c>)에서는 모두 켜졌다고 본다.</b> 나침반이
    /// 꺼지는지는 레벨 계산 뒤에야 알 수 있는데, 그 레벨이 다시 신비 각인 수(콤보 수)에 달려 있다.
    /// 게임은 이 고리를 <c>ReleasePermission</c> 을 많아야 세 바퀴 돌려 푼다. 우리는 한 바퀴만 도는
    /// 어림이다.
    /// </summary>
    internal readonly struct CharmActivity
    {
        private readonly SimulationResult? _result;
        private readonly GridSpec _grid;
        private readonly GridOccupancy? _occupancy;

        internal CharmActivity(SimulationResult result, GridSpec grid, GridOccupancy occupancy)
        {
            _result = result;
            _grid = grid;
            _occupancy = occupancy;
        }

        internal bool IsActive(CharmSlot charm, GridPos cell) =>
            _result is null ||
            PlacementSolver.Reason(charm, cell, _result, _grid, _occupancy!) == CharmInactiveReason.None;
    }

    /// <summary>
    /// 나침반이 이어진 뿌리 하나. <see cref="Breaker"/> 는 길에서 출발한 나침반에 가장 가까운 돌파의 나침반이다.
    /// </summary>
    internal readonly struct CompassRoot
    {
        internal CompassRoot(CharmSlot charm, GridPos cell, CharmSlot? breaker)
        {
            Charm = charm;
            Cell = cell;
            Breaker = breaker;
        }

        internal CharmSlot Charm { get; }
        internal GridPos Cell { get; }
        internal CharmSlot? Breaker { get; }
    }

    /// <summary>돌파의 나침반 아래 침이 그 돌파를 지나 닿는 뿌리 하나.</summary>
    internal readonly struct LiftedRoot
    {
        internal LiftedRoot(CharmSlot needle, GridPos needleCell, CharmSlot root)
        {
            Needle = needle;
            NeedleCell = needleCell;
            Root = root;
        }

        internal CharmSlot Needle { get; }
        internal GridPos NeedleCell { get; }
        internal CharmSlot Root { get; }
    }

    /// <summary>
    /// 1.1.0 의 나침반(<c>Charm_Compass</c>, <c>Charm_CompassDamageUp</c>, <c>Charm_CompassBreakthrough</c>).
    /// 1.0.33 의 북향의 침(<c>Charm_UpCharmDamage</c>)을 대신한다. 다른 점은 docs/RESEARCH.md 의 "침은 1.1.0
    /// 에서 나침반이 됐다" 에 있다 - 부모 칸이 여럿이고, 카테고리는 이어진 뿌리 전부의 합집합이며, 꺼진
    /// 나침반은 제 몫이 없고 사슬도 끊는다. 돌파는 그 아래 나침반의 희귀도 조건을 없앤다.
    ///
    /// 두 판을 함께 지원하므로 어느 규칙인지는 카탈로그의 동작 클래스 이름이 정한다.
    /// </summary>
    internal static class Compass
    {
        private const string Distribute = "Charm_Compass";
        private const string DamageUp = "Charm_CompassDamageUp";
        private const string Breakthrough = "Charm_CompassBreakthrough";

        internal static bool Is(CharmDefinition definition) =>
            definition.Behavior == Distribute || definition.Behavior == DamageUp || definition.Behavior == Breakthrough;

        internal static bool Lifts(CharmDefinition definition) => definition.Behavior == Breakthrough;

        /// <summary>
        /// 꺼지지 않은 이 나침반에서 위로 걸어 닿는 뿌리들. 게임 <c>Charm_Compass.WalkToRoots</c> 와 같은
        /// 걸음이다 - 부모 칸마다 깊이 우선으로 가고, 꺼진 나침반에서 끊고, 방문 표시는 탐색 전체에 하나다.
        /// 뿌리는 길의 나침반 전부가 인정해야 하는데(<c>IsDependencyValid</c>) 나침반은 모두 같은 판정이라
        /// 뿌리 하나만 보면 된다.
        ///
        /// 피해 보너스는 게임이 뿌리에서 아래로 BFS 를 돌며(<c>RequestCharmDamageBonusOnRoot</c>) 같은 길을
        /// 거꾸로 걷는다. 돌파의 몫은 BFS 가 처음 닿은 길의 것인데, 같은 뿌리에 닿는 길이 둘 이상이면 어느
        /// 길이 먼저인지는 의존성 등록 순서라 알 수 없다. 그래서 깊이 우선으로 먼저 걸은 길의 것을 쓴다(어림).
        /// </summary>
        internal static void Roots(
            CharmSlot compass, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot> neighbors,
            in CharmActivity activity, List<CompassRoot> into)
        {
            into.Clear();
            if (!activity.IsActive(compass, cell)) return;

            var visited = _visited ??= new List<CharmSlot>();
            visited.Clear();
            visited.Add(compass);
            Walk(compass, cell, compass, cell, neighbors, activity, Lifts(compass.Definition) ? compass : null, visited, into);
        }

        // 한 호출 안에서 채우고 다 읽는다. 걸음은 저 자신만 부른다.
        [ThreadStatic] private static List<CharmSlot>? _visited;
        [ThreadStatic] private static List<CompassRoot>? _roots;
        [ThreadStatic] private static List<CompassRoot>? _liftedRoots;

        /// <summary>
        /// <paramref name="moved"/> 를 <paramref name="movedCell"/> 에 세운 판에서 그 칸의 아티팩트.
        /// 배정은 직전 반복의 이웃으로 채점하고 자리를 맞바꾸므로, 옮긴 것의 옛 칸에는 지금 목표 칸에 있던
        /// 아티팩트가 선다 - 1.0.33 침의 걸음(PositionalWorth.Step)과 같은 갈음.
        /// </summary>
        private static bool At(
            IReadOnlyDictionary<GridPos, CharmSlot> neighbors, CharmSlot moved, GridPos movedCell,
            GridPos cell, out CharmSlot charm)
        {
            if (cell == movedCell)
            {
                charm = moved;
                return true;
            }
            if (!neighbors.TryGetValue(cell, out charm)) return false;
            return charm != moved || neighbors.TryGetValue(movedCell, out charm) && charm != moved;
        }

        private static void Walk(
            CharmSlot moved, GridPos movedCell, CharmSlot current, GridPos at,
            IReadOnlyDictionary<GridPos, CharmSlot> neighbors, in CharmActivity activity, CharmSlot? breaker,
            List<CharmSlot> visited, List<CompassRoot> into)
        {
            foreach (var offset in current.Definition.DependencyParents)
            {
                var parentCell = at.Offset(offset.X, offset.Y);
                if (!At(neighbors, moved, movedCell, parentCell, out var parent)) continue;
                if (visited.Contains(parent)) continue;
                visited.Add(parent);

                if (Is(parent.Definition))
                {
                    if (!activity.IsActive(parent, parentCell)) continue;
                    Walk(moved, movedCell, parent, parentCell, neighbors, activity,
                        breaker ?? (Lifts(parent.Definition) ? parent : null), visited, into);
                    continue;
                }

                if (IsValidRoot(parent)) into.Add(new CompassRoot(parent, parentCell, breaker));
            }
        }

        /// <summary>
        /// 이 돌파의 나침반을 지나 뿌리에 닿는 아래 침들. 길에 돌파가 여럿이면 침에 가장 가까운 것이 받는다.
        /// 돌파 자리를 바꿔 보는 배정이 부르므로 침의 칸도 <see cref="At"/> 의 맞바꾸기로 읽는다.
        /// </summary>
        internal static void Lifted(
            CharmSlot breakthrough, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot> neighbors,
            in CharmActivity activity, List<LiftedRoot> into)
        {
            into.Clear();
            if (!activity.IsActive(breakthrough, cell)) return;

            var hasOldCell = false;
            var oldCell = default(GridPos);
            foreach (var pair in neighbors)
            {
                if (pair.Value != breakthrough) continue;
                hasOldCell = true;
                oldCell = pair.Key;
                break;
            }

            var roots = _liftedRoots ??= new List<CompassRoot>();
            var visited = _visited ??= new List<CharmSlot>();
            foreach (var pair in neighbors)
            {
                var needle = pair.Value;
                if (needle == breakthrough || !Is(needle.Definition) || !PositionalWorth.IsNeedle(needle.Definition)) continue;
                var needleCell = pair.Key;
                if (needleCell == cell)
                {
                    if (!hasOldCell) continue;
                    needleCell = oldCell;
                }
                if (needle.IsDormant || !activity.IsActive(needle, needleCell)) continue;

                roots.Clear();
                visited.Clear();
                visited.Add(needle);
                Walk(breakthrough, cell, needle, needleCell, neighbors, activity, null, visited, roots);
                foreach (var root in roots)
                {
                    if (root.Breaker != breakthrough || root.Charm.IsDormant || !activity.IsActive(root.Charm, root.Cell)) continue;
                    into.Add(new LiftedRoot(needle, needleCell, root.Charm));
                }
            }
        }

        /// <summary>
        /// 게임 <c>Charm_Compass.IsDependencyValid</c> 가 나침반이 아닌 아티팩트에 내리는 판정. 뿌리가 켜졌는지는 보지 않는다 -
        /// 카테고리(<c>WalkToRoots</c>)는 무기가 안 맞아 꺼진 뿌리의 것도 모은다. 피해 쪽은 뿌리마다 활성을 따로 본다.
        /// </summary>
        private static bool IsValidRoot(CharmSlot root) =>
            !root.IsFiller && (root.IsAttackable ?? root.Definition.IsAttackable);

        /// <summary>
        /// 이 나침반이 내보이는 카테고리. 게임 <c>OnPreSetEffectRefreshed</c> 가 이어진 뿌리의
        /// <c>GetItemCategory()</c> 를 합집합으로 모은다. 꺼져 있으면 아무것도 없다.
        /// </summary>
        internal static void AddCategories(
            CharmSlot compass, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot> neighbors,
            in CharmActivity activity, List<string> into)
        {
            var roots = _roots ??= new List<CompassRoot>();
            Roots(compass, cell, neighbors, activity, roots);
            if (roots.Count == 0) return;

            var start = into.Count;
            var found = _found ??= new List<string>();
            foreach (var root in roots)
            {
                found.Clear();
                PositionalWorth.AddCategories(root.Charm, root.Cell, found);
                foreach (var category in found)
                {
                    if (into.IndexOf(category, start) < 0) into.Add(category);
                }
            }
        }

        [ThreadStatic] private static List<string>? _found;

        /// <summary>
        /// 나침반이 제 값을 하는 배수. 침(<c>Charm_CompassDamageUp</c>)은 이어진 뿌리마다 보너스를 주므로
        /// 1.0.33 침의 배수(<see cref="PositionalWorth.DependencyFactor"/>)를 뿌리마다 더한다 - 분배의 나침반
        /// 아래 침은 두 뿌리에 같은 몫을 준다.
        ///
        /// 침의 배수는 돌파가 없을 때의 것이다. 돌파가 희귀도 조건을 풀어 더 받는 몫은 그 돌파의 값어치로
        /// 센다(<see cref="Lifted"/>) - 침에 주면 돌파는 어디에 서든 0 이라 침과 뿌리 사이로 끌려올 까닭이
        /// 없고, 아래에 침이 없는 돌파에 뿌리 몫을 주면 쓸모없는 자리를 차지하러 간다(제보 <c>eebed530</c>:
        /// 지정 대상 대신 쿠나이 밑). 그래서 돌파 자신의 배수는 0 이다.
        ///
        /// 분배는 제 몫이 없고 아래 침이 그 몫을 받는다. 그 몫은 아래 침의 배수에 이미 들어가므로 여기서는
        /// <b>어림으로</b> 가장 값진 뿌리의 몫만 준다. 0 으로 두면 어디에 서든 같아져 이어지지 않는 칸에
        /// 버려진다.
        /// </summary>
        internal static double Factor(
            PlacementProblem problem, CharmSlot compass, GridPos cell, int level,
            IReadOnlyDictionary<GridPos, CharmSlot> neighbors, in CharmActivity activity)
        {
            if (Lifts(compass.Definition)) return 0;

            var roots = _roots ??= new List<CompassRoot>();
            Roots(compass, cell, neighbors, activity, roots);

            var damage = PositionalWorth.IsNeedle(compass.Definition);
            var factor = 0.0;
            foreach (var root in roots)
            {
                if (root.Charm.IsDormant || !activity.IsActive(root.Charm, root.Cell)) continue;

                factor = damage
                    ? factor + PositionalWorth.TargetFactor(problem, compass, root.Charm, level, bypassRarity: false)
                    : Math.Max(factor, PositionalWorth.TargetShare(problem, root.Charm));
            }
            return factor;
        }

        /// <summary>
        /// 이 자리에서 이어진 켜진 뿌리. 지정한 강화 대상이 있으면 그것을 먼저 고른다 - 나침반은 뿌리를
        /// 여럿 가질 수 있어 하나만 돌려주면 지정이 닿았는데도 못 닿았다고 셀 수 있다.
        /// </summary>
        internal static bool ConnectedRoot(
            CharmSlot compass, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot> neighbors,
            in CharmActivity activity, out CharmSlot target)
        {
            target = compass;
            var roots = _roots ??= new List<CompassRoot>();
            Roots(compass, cell, neighbors, activity, roots);

            var found = false;
            foreach (var root in roots)
            {
                if (root.Charm.IsDormant || !activity.IsActive(root.Charm, root.Cell)) continue;
                if (!found || root.Charm.IsSupportTarget && !target.IsSupportTarget) target = root.Charm;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// 둘을 함께 옮겨 보는 다듬기와 지정 탐색이 쓰는 대표 방향. 분배의 나침반은 부모가 둘인데 그 가운데
        /// 첫 칸만 시도한다 - 시도 후보를 고르는 데만 쓰고 채점은 모든 부모를 본다.
        /// </summary>
        internal static (int X, int Y) PrimaryOffset(CharmDefinition definition) =>
            definition.DependencyParents.Count > 0
                ? (definition.DependencyParents[0].X, definition.DependencyParents[0].Y)
                : (0, -1);
    }
}
