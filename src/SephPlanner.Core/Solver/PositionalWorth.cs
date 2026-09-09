using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 자리에 따라 하는 일이 달라지는 아티팩트들의 자리 가치.
    ///
    /// 여기 있는 규칙은 전부 게임 클래스를 디컴파일해 옮긴 것이고, <b>사람이 대상을 고르지
    /// 않는다</b> - 게임이 자리만 보고 정한다. 그래서 솔버가 알아서 좋은 자리를 찾을 수 있다.
    ///
    /// 이웃이 <see cref="PlacementSolver"/>의 직전 반복 배정에서 오는 근사라는 점은 하얀 종이·
    /// 조화의 수정과 같다. 이미 모여 있는 자리를 찾아갈 뿐, 이웃을 이 아티팩트 주위로 다시
    /// 모으는 탐색까지는 하지 못한다.
    /// </summary>
    public static class PositionalWorth
    {
        /// <summary>이웃 여덟 칸. 게임 <c>Charm_PlanetModule.directions</c>와 같은 집합이다.</summary>
        private static readonly (int X, int Y)[] Around =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1),
        };

        /// <summary>
        /// 강화 한 번을 그 아티팩트의 레벨 한 칸으로 친다.
        ///
        /// <b>잰 값이 아니다.</b> 거대한 망원경의 행성 강화도 헌신의 휘장의 혼돈 모드도 게임에서
        /// 능력치 수치가 아니라 소환물의 동작을 바꾸는 것이라, 정적 데이터로는 크기가 안 나온다.
        /// 방향(모아 두는 쪽이 낫다)만은 확실하므로 크기는 가장 보수적인 어림으로 두었다 -
        /// 레벨 한 칸이면 콤보 한 단계(<see cref="Worth.ComboThreshold"/> = 3.4)의 1/3 남짓이라,
        /// 실제로 잰 값을 뒤집지 못한다. 재고 나면 여기만 고치면 된다.
        /// </summary>
        public const double EnhanceStep = 1.0;

        /// <summary>
        /// 북향의 침이 제 값을 하는가. 게임 <c>Charm_UpCharmDamage.OnRequestCharmDamageBonus</c>가
        /// 대상을 못 찾으면 0을 돌려주므로, 대상 없는 침은 자리만 차지하고 아무 일도 하지 않는다.
        ///
        /// 돌려주는 것은 배수다. 0이면 헛자리, 1이면 제 몫, 그보다 크면 레어도 조건
        /// (<c>hasDependencyCondition</c>)까지 맞아 덤이 붙은 자리다. 침이 아닌 아티팩트는 1이다.
        /// </summary>
        public static double DependencyFactor(
            CharmSlot charm, GridPos cell, int level, IReadOnlyDictionary<GridPos, CharmSlot>? neighbors)
        {
            if (!IsNeedle(charm.Definition)) return 1;
            if (neighbors is null) return 1;

            if (!DependencyTarget(charm, cell, neighbors, out var target, out _)) return 0;

            var definition = charm.Definition;
            if (!definition.HasDependencyCondition) return 1;
            if (target.Definition.Rarity > definition.DependencyMaxRarity) return 1;

            var baseBonus = At(definition.DependencyBonusByLevel, level);
            var extra = At(definition.DependencyExtraByLevel, level);
            if (baseBonus <= 0) return extra > 0 ? 2 : 1;

            return 1 + extra / baseBonus;
        }

        /// <summary>
        /// 자리 때문에 더 내보이는 카테고리로 나아가는 콤보 값어치. 침은 대상에게 물려받은 것(게임
        /// <c>SearchCategory</c>가 대상의 <c>GetItemCategory()</c>를 자기 것으로 삼는다), 캘세더니
        /// 열쇠는 행이 정한 것(<c>lineCategory[YIdx % 개수]</c>), 하얀 종이는 양옆이 공유하는 것들이다.
        /// 콤보를 세는 <c>SearchSetEffectInInventory</c>가 그 값을 읽으므로 <b>이들에 한해서는 콤보
        /// 개수가 배치에 달려 있다</b>(docs/RESEARCH.md). 출발 개수는 자기 몫을 뺀 것이어야 한다 -
        /// <see cref="ComboCounting"/>.
        /// </summary>
        public static double ComboWorth(
            PlacementProblem problem, CharmSlot charm, GridPos cell,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors)
        {
            if (problem.Combos is null || !ComboCounting.IsPositional(charm.Definition)) return 0;

            // 열쇠는 이웃이 없어도 행만으로 정해진다. 침과 종이는 이웃이 있어야 한다.
            if (neighbors is null && charm.Definition.LineCategories.Count == 0) return 0;

            var categories = new List<string>(2);
            ComboCounting.PositionalCategories(
                charm, cell, neighbors ?? EmptyNeighbors, categories);

            var worth = 0.0;
            foreach (var category in categories)
            {
                if (string.IsNullOrEmpty(category)) continue;

                var combo = problem.Combos(category);
                if (combo is null) continue;

                worth += Worth.OfComboStep(combo, ComboCounting.CountFor(problem, charm, category, neighbors), out _, out _);
            }
            return worth;
        }

        private static readonly IReadOnlyDictionary<GridPos, CharmSlot> EmptyNeighbors =
            new Dictionary<GridPos, CharmSlot>();

        /// <summary>캘세더니 열쇠가 그 행에서 내보이는 카테고리. <c>lineCategory[YIdx % 개수]</c>.</summary>
        internal static string LineCategory(CharmDefinition definition, GridPos cell)
        {
            var line = definition.LineCategories;
            return line[((cell.Y % line.Count) + line.Count) % line.Count];
        }

        /// <summary>
        /// 거대한 망원경(<c>Charm_PlanetModule</c>)처럼 이웃 여덟 칸의 같은 카테고리 아티팩트를
        /// 강화하는 것. 자기 레벨은 보지 않고 몇을 감쌌는지만 본다 - 게임도 그렇다.
        /// </summary>
        public static double NeighborEnhanceWorth(
            CharmSlot charm, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot>? neighbors)
        {
            var category = charm.Definition.NeighborEnhanceCategory;
            if (category.Length == 0 || neighbors is null) return 0;

            var worth = 0.0;
            foreach (var (dx, dy) in Around)
            {
                if (!neighbors.TryGetValue(cell.Offset(dx, dy), out var neighbor)) continue;
                if (neighbor == charm || neighbor.IsFiller || neighbor.IsDormant) continue;
                if (!neighbor.Definition.Categories.Contains(category)) continue;

                worth += EnhanceStep * neighbor.Worth.LevelStep;
            }
            return worth;
        }

        /// <summary>
        /// 헌신의 휘장(<c>Charm_CompanionChaos</c>). 게임 <c>SearchCompanion</c>이 <b>같은 행을
        /// 끝에서 끝까지</b> 훑어 동료를 전부 혼돈 모드로 돌린다. 정원 같은 상한은 없다.
        /// </summary>
        public static double RowCompanionWorth(
            CharmSlot charm, GridPos cell, GridSpec grid, IReadOnlyDictionary<GridPos, CharmSlot>? neighbors)
        {
            if (charm.Definition.Behavior != "Charm_CompanionChaos" || neighbors is null) return 0;

            var worth = 0.0;
            for (var x = 0; x < grid.Width; x++)
            {
                if (!neighbors.TryGetValue(new GridPos(x, cell.Y), out var neighbor)) continue;
                if (neighbor == charm || neighbor.IsFiller || neighbor.IsDormant) continue;
                if (!neighbor.Definition.IsCompanion) continue;

                worth += EnhanceStep * neighbor.Worth.LevelStep;
            }
            return worth;
        }

        /// <summary>
        /// 열쇠는 행 카테고리, 침은 이웃 배치가 주어지면 사슬 끝 대상의 카테고리를 반환한다.
        /// 이웃 없는 조회는 탐색 후보를 좁히는 용도이며 종이 중첩의 갱신 순서는 별도다.
        /// </summary>
        internal static IEnumerable<string> CategoriesOf(
            CharmSlot charm, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot>? neighbors = null)
        {
            // 종이끼리는 갱신 순서와 이전 상태에 의존하므로 정적 정의 대신 최근 관측을 쓴다.
            if (charm.Definition.Behavior == "Charm_WhitePaper")
                return charm.ObservedCategories ?? (IEnumerable<string>)System.Array.Empty<string>();
            if (neighbors is not null && IsNeedle(charm.Definition))
                return DependencyTarget(charm, cell, neighbors, out var target, out var targetCell)
                    ? CategoriesOf(target, targetCell, neighbors)
                    : System.Array.Empty<string>();
            if (charm.Definition.LineCategories.Count == 0) return charm.Definition.Categories;

            return new[] { LineCategory(charm.Definition, cell) };
        }

        internal static bool IsNeedle(CharmDefinition definition) =>
            definition.DependencyBonusByLevel.Count > 0;

        /// <summary>
        /// 침이 실제로 강화하게 되는 아티팩트. 게임 <c>SearchCategory</c>와 같은 걸음이다 -
        /// 침 위에 침이 있으면 그 침의 오프셋을 따라 계속 올라가고, 침이 아닌 아티팩트에서 멈춘다.
        /// 도중에 빈 칸을 만나거나 같은 칸을 두 번 밟으면(고리) 아무도 강화하지 못한다.
        /// </summary>
        internal static bool DependencyTarget(
            CharmSlot charm, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot> neighbors,
            out CharmSlot target, out GridPos targetCell)
        {
            target = charm;
            targetCell = cell;

            // 사슬은 대개 없거나 한둘이다. 배정 비용 행렬의 최내곽에서 도는 자리라 집합·목록을
            // 미리 만들지 않고, 사슬이 생길 때만 자리를 잡는다.
            List<CharmSlot>? chain = null;
            List<GridPos>? seen = null;
            var current = charm;
            var at = cell;

            while (IsNeedle(current.Definition))
            {
                at = at.Offset(current.Definition.DependencyOffsetX, current.Definition.DependencyOffsetY);

                if (seen is null) seen = new List<GridPos>(2);
                if (seen.Contains(at)) return false;
                seen.Add(at);

                if (!neighbors.TryGetValue(at, out var next)) return false;

                // 직전 반복에서 자기가 서 있던 칸이 대상으로 잡히는 경우다. 배정은 자리 맞바꾸기라
                // 실제로는 지금 목표 칸에 있는 아티팩트가 그 자리를 채우게 된다 - 조화의 수정이
                // 이웃을 셀 때와 같은 갈음이다.
                if (next == charm && !neighbors.TryGetValue(cell, out next)) return false;
                if (next == charm) return false;

                current = next;
                if (IsNeedle(current.Definition)) (chain ??= new List<CharmSlot>(2)).Add(current);
            }

            // 게임은 사슬의 침 전부가 마지막 아티팩트를 대상으로 인정할 때만 물려받는다.
            if (!IsValidTarget(charm, current)) return false;
            if (chain is not null)
            {
                foreach (var needle in chain)
                {
                    if (!IsValidTarget(needle, current)) return false;
                }
            }

            target = current;
            targetCell = at;
            return true;
        }

        /// <summary>게임 <c>Charm_UpCharmDamage.IsDependencyValid</c>와 같은 판정이다.</summary>
        private static bool IsValidTarget(CharmSlot needle, CharmSlot target)
        {
            if (needle == target) return false;
            if (IsNeedle(target.Definition)) return true;
            if (target.IsFiller || target.IsDormant) return false;

            return target.IsAttackable ?? target.Definition.IsAttackable;
        }

        private static double At(List<double> table, int level)
        {
            if (table.Count == 0) return 0;
            if (level < 0) level = 0;
            return table[level < table.Count ? level : table.Count - 1];
        }
    }
}
