using System;
using System.Collections.Generic;
using SephPlanner.Core.Charms;
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
    /// 조화의 수정과 같다. 이웃 강화는 받는 쪽의 자리 배정에도 반영해 주변으로 모을 수 있다.
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
        /// <b>잰 값이 아니다.</b> 헌신의 휘장의 혼돈 모드(<c>SetChaoticMode</c>)는 능력치 수치가
        /// 아니라 소환물의 동작을 바꾸는 것이라 정적 데이터로 크기가 안 나온다. 방향(모아 두는
        /// 쪽이 낫다)만은 확실하므로 크기는 가장 보수적인 어림으로 두었다 - 레벨 한 칸이면 콤보
        /// 한 단계(<see cref="Worth.ComboThreshold"/>, 기본 2.59)의 5분의 2 남짓이라, 실제로 잰
        /// 값을 뒤집지 못한다.
        ///
        /// <b>망원경 쪽은 이제 잰다</b>(<see cref="EnlargeSteps"/>). 이 값은 휘장의 것이고,
        /// 행성의 피해량 표가 없는 옛 카탈로그에서 물러설 자리로도 쓴다.
        /// </summary>
        public const double EnhanceStep = 1.0;

        /// <summary>
        /// 북향의 침이 제 값을 하는가. 게임 <c>Charm_UpCharmDamage.OnRequestCharmDamageBonus</c>가
        /// 대상을 못 찾으면 0을 돌려주므로, 대상 없는 침은 자리만 차지하고 아무 일도 하지 않는다.
        ///
        /// 돌려주는 것은 배수다. 0이면 헛자리이고, 그보다 크면 강화할 대상을 찾은 자리다. 침이
        /// 아닌 아티팩트는 1이다.
        ///
        /// <b>덤은 대상이 받을 만할 때만 값이 있다.</b> 레어도 덤은 대상에게 주는 피해 보너스인데,
        /// 예전에는 대상이 누구든 같은 배수였다 - 그래서 침이 <b>사용자가 낮춘 아티팩트를 우선해서</b>
        /// 강화했다(제보 `fee2feb9`: 별 −2 짜리 위에 침이 붙고, 조건을 만족하는 여섯 대상 가운데
        /// 다섯이 낮춰진 것이었다). 조건만 맞으면 값싼 것을 죽은 칸에 버려도 배수를 다 받았기
        /// 때문이다. 이제 덤은 대상의 가중치만큼만 쳐 준다.
        ///
        /// <b>올린 쪽으로는 키우지 않는다</b>(<c>Math.Min</c>). 선호 대상이라고 덤을 부풀리면 침이
        /// 그 아티팩트를 좋은 칸에서 제 위 칸으로 끌어내리게 되는데, 그것이 지금 고치는 것보다 나쁘다.
        /// 그래서 가중치 1 이상은 전과 같은 값이고, <b>낮춘 대상에서만 덤이 줄어든다.</b>
        ///
        /// <b>그리고 대상이 실제로 얼마나 센지를 곱한다</b>(<see cref="TargetShare"/>). 침이 주는
        /// 것은 <b>대상의 피해에 대한 백분율</b>인데 예전에는 백분율만 세고 그 백분율이 걸리는
        /// 대상을 보지 않았다. 실제 카탈로그에서 덤은 기본의 2.5배(6~10% 에 15~25%)이고 조건은
        /// 레어도 언커먼 이하라, 공격 가능한 아티팩트 72종 중 41종이 해당한다 - 그래서 침이 주력
        /// 대신 흔한 아티팩트를 3.5배로 선호했다. 대상 값어치의 몫을 곱하면 "약한 것의 35%" 와
        /// "센 것의 10%" 를 같은 자로 견주게 된다. 대상이 하나뿐이면 몫이 1이라 전과 같은 값이다.
        /// </summary>
        public static double DependencyFactor(
            PlacementProblem problem, CharmSlot charm, GridPos cell, int level,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors)
        {
            if (!IsNeedle(charm.Definition)) return 1;
            if (neighbors is null) return 1;

            if (!DependencyTarget(charm, cell, neighbors, out var target, out _)) return 0;

            var share = TargetShare(problem, target);
            var definition = charm.Definition;
            if (!definition.HasDependencyCondition) return share;
            if (target.Definition.Rarity > definition.DependencyMaxRarity) return share;

            var wanted = Math.Min(1, target.Weight);
            var baseBonus = At(definition.DependencyBonusByLevel, level);
            var extra = At(definition.DependencyExtraByLevel, level);
            if (baseBonus <= 0) return (extra > 0 ? 1 + wanted : 1) * share;

            return (1 + extra / baseBonus * wanted) * share;
        }

        /// <summary>
        /// 이 대상이 판에서 가장 값진 대상의 몇 몫인가. 자리에 따라 흔들리지 않도록 둘 다 상한
        /// 레벨로 잰다 - 대상이 옮겨 다닐 때마다 침의 값어치가 따라 흔들리면 배치가 폴링마다
        /// 달라진다. 잰 것은 값어치이지 피해량이 아니므로 어림이다.
        /// </summary>
        private static double TargetShare(PlacementProblem problem, CharmSlot target)
        {
            var best = problem.NeedleTargetWorth;
            if (best <= 0) return 1;

            return Math.Min(1, Math.Max(0, target.Worth.At(target.Definition.MaxLevel)) / best);
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
                if (string.IsNullOrEmpty(category) || problem.ScoresComboItself(category)) continue;

                var combo = problem.Combos(category);
                if (combo is null) continue;

                worth += problem.Scale.OfComboStep(
                    combo, ComboCounting.CountFor(problem, charm, category, neighbors), out _, out _);
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
        /// 이웃 강화가 실제로 걸리는 charm 타입. 카탈로그 23 부터는
        /// <see cref="CharmDefinition.IsSummonPlanet"/> 가 답하고, 이 이름은 그 항목이 없는
        /// 옛 자료에서만 쓴다 - <b>이름 비교로는 하위 클래스를 놓치기 때문에</b> 새 자료에서는
        /// 쓰지 않는다(게임은 <c>is</c> 로 보고 <c>Charm_SummonRedPlanet</c> 도 거대화한다).
        /// </summary>
        private const string EnhanceableBehavior = "Charm_SummonGreenBat";

        private static bool Enlargeable(CharmDefinition definition) =>
            definition.IsSummonPlanet || definition.Behavior == EnhanceableBehavior;

        /// <summary>
        /// 거대한 망원경(<c>Charm_PlanetModule</c>)처럼 이웃 여덟 칸의 같은 카테고리 아티팩트를
        /// 강화하는 것. 자기 레벨은 보지 않고 몇을 감쌌는지만 본다 - 게임도 그렇다.
        ///
        /// <b>카테고리만으로는 대상이 되지 않는다.</b> 게임의 <c>SearchPlanet</c> 은 그 칸의
        /// <c>Entity.categories</c> 에 <c>PLANET</c> 이 있고 <b>그 칸의 Charm 이
        /// <c>Charm_SummonGreenBat</c>(하위 클래스 포함) 일 때만</b> <c>SetEnhancement</c> 를
        /// 부른다(1.0.33 디컴파일).
        /// 카탈로그의 <c>PLANET</c> 열하나 가운데 넷 - 거대 망원경 자신, 혜성, 악보 '은하',
        /// 붉은행성 관찰일지 - 이 그 타입이 아니다. 카테고리만 보면 망원경이 거대화하지도 못할 것
        /// 옆에 앉아 진짜 행성이 설 자리를 가져간다.
        ///
        /// 카테고리는 <b>정의의 것</b>을 본다. 게임도 <c>Entity.categories</c> 를 읽으므로 하얀
        /// 종이가 물려받은 카테고리는 여기 섞이지 않는다.
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
                worth += EnhancementWorth(charm, neighbor);
            }
            return worth;
        }

        // 배정할 때는 받는 쪽의 이동 이득도 센다. 최종 점수에는 주는 쪽에서 한 번만 더한다.
        internal static double ReceivedEnhanceWorth(
            CharmSlot charm, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot>? neighbors,
            SimulationResult result, GridSpec grid, GridOccupancy occupancy)
        {
            if (neighbors is null || charm.IsFiller || charm.IsDormant || !Enlargeable(charm.Definition)) return 0;

            var worth = 0.0;
            foreach (var (dx, dy) in Around)
            {
                var helperCell = cell.Offset(dx, dy);
                if (!neighbors.TryGetValue(helperCell, out var helper) || helper == charm ||
                    helper.IsFiller || helper.IsDormant || helper.Definition.NeighborEnhanceCategory.Length == 0) continue;
                if (PlacementSolver.Reason(helper, helperCell, result, grid, occupancy) != CharmInactiveReason.None) continue;
                worth += CharmWorth.ApplyWeight(EnhancementWorth(helper, charm), helper.Weight);
            }
            return worth;
        }

        private static double EnhancementWorth(CharmSlot helper, CharmSlot target) =>
            helper != target && !target.IsFiller && !target.IsDormant && Enlargeable(target.Definition) &&
            target.Definition.Categories.Contains(helper.Definition.NeighborEnhanceCategory)
                ? EnlargeSteps(target) * target.Worth.LevelStep : 0;

        /// <summary>
        /// 거대화 한 번이 그 행성의 <b>레벨 몇 칸</b>인가. 게임은 <c>GreenBat</c> 이 쏘는 순간
        /// <c>num += num * 0.5f</c> 로 그때의 피해량에 곱으로 걸므로, 레벨별 피해량 표
        /// (<c>damageByLevel</c>)와 견주면 크기가 나온다. 북향의 침이 두 표의 비율만 쓰는 것과
        /// 같은 자리라 피해량을 값어치 단위로 옮길 필요가 없다.
        ///
        /// <b>레벨 0 으로 잰다.</b> 곱이라서 실제로는 레벨이 오를수록 커지지만(푸른 행성 1.25칸 →
        /// 상한 3.75칸), 이웃의 지금 레벨을 보면 배치가 바뀔 때마다 망원경의 값어치가 따라 흔들린다 -
        /// 침의 <see cref="TargetShare"/> 가 자리에 흔들리지 않으려고 상한 레벨로 재는 것과 같은
        /// 이유다. 둘 중 아래로 잡는 쪽을 골랐으므로 이 값은 언제나 실제보다 작거나 같다.
        ///
        /// 표가 없으면 - 소환 행성이 아니거나 카탈로그 22 이하로 모은 자료 - <see cref="EnhanceStep"/>
        /// 으로 물러선다. 1.25 는 일곱 행성 가운데 가장 작은 값이라 그 물러섬도 위로 넘지 않는다.
        /// </summary>
        private static double EnlargeSteps(CharmSlot planet)
        {
            var damage = planet.Definition.SummonDamageByLevel;
            if (damage.Count < 2 || damage[0] <= 0) return EnhanceStep;

            // 게임은 표를 SafeRandomAccess(maxLevel) 로 읽으므로 상한 위의 남는 칸은 보지 않는다.
            var top = Math.Min(planet.Definition.MaxLevel, damage.Count - 1);
            if (top < 1) return EnhanceStep;

            var step = (damage[top] - damage[0]) / (double)top;
            if (step <= 0) return EnhanceStep;

            return 0.5 * damage[0] / step;
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

        public static bool IsNeedle(CharmDefinition definition) =>
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
