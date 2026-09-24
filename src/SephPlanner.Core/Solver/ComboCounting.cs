using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 자리에 따라 카테고리가 달라지는 아티팩트(북향의 침, 캘세더니 열쇠, 하얀 종이)를 콤보 개수에
    /// 어떻게 셀 것인가.
    ///
    /// 게임의 <c>SearchSetEffectInInventory</c>는 아티팩트마다 <c>GetItemCategory()</c>를 읽어
    /// 세므로 침이 물려받은 카테고리도 개수에 들어간다. 그래서 <b>스냅샷의 개수에는 이들의 지금
    /// 자리 몫이 이미 섞여 있다.</b> 그 개수로 "하나 더 모으면"을 재면 자기 몫을 겹쳐 세게 되고,
    /// 실제로 그것이 2주기를 만들었다 - 침 둘이 FLAMESWORD(11, 임계값을 다 넘김) 아래에서
    /// EMBER(8) 아래로 가면 게임이 EMBER 10 / FLAMESWORD 9 로 다시 세고, 다음 계산은 "FLAMESWORD
    /// 하나 더면 10" 이라며 침을 도로 부른다. F8 을 누를 때마다 6~7수가 나온 판이 그것이다.
    ///
    /// 그래서 자리 몫을 뺀 바탕 개수를 만들고, 평가하는 배치에서 <b>다른</b> 자리 의존 아티팩트가
    /// 보태는 몫만 다시 더한다. 자기 몫은 넣지 않는다 - <see cref="Worth.OfComboStep"/>이 "하나 더"
    /// 로 센다.
    /// </summary>
    public static class ComboCounting
    {
        /// <summary>
        /// 이 아티팩트가 자리 때문에 더 내보이는 카테고리. 침은 대상에게 물려받은 것, 열쇠는 행이
        /// 정한 것 하나, 하얀 종이는 양옆이 공유하는 것들이다. 정의 자체의 카테고리는 넣지 않는다.
        /// </summary>
        public static void PositionalCategories(
            CharmSlot charm, GridPos cell, IReadOnlyDictionary<GridPos, CharmSlot> neighbors, List<string> into)
        {
            var definition = charm.Definition;
            if (definition.LineCategories.Count > 0)
            {
                into.Add(PositionalWorth.LineCategory(definition, cell));
                return;
            }

            if (PositionalWorth.IsNeedle(definition))
            {
                if (PositionalWorth.DependencyTarget(charm, cell, neighbors, out var target, out var targetCell))
                    into.AddRange(PositionalWorth.CategoriesOf(target, targetCell));
                return;
            }

            if (definition.Behavior != "Charm_WhitePaper") return;
            var matches = new Dictionary<string, int>();
            foreach (var offset in new[] { 1, -1 })
            {
                var at = cell.Offset(offset, 0);
                if (!neighbors.TryGetValue(at, out var neighbor) || neighbor == charm || neighbor.IsFiller) continue;
                foreach (var category in PositionalWorth.CategoriesOf(neighbor, at, neighbors)) Add(matches, category, 1);
            }
            foreach (var match in matches)
                if (match.Value >= definition.PaperMatch) into.Add(match.Key);
        }

        /// <summary>자리에 따라 카테고리가 달라지는 아티팩트인가. 아니면 개수 셈에서 볼 것이 없다.</summary>
        public static bool IsPositional(CharmDefinition definition) =>
            definition.LineCategories.Count > 0 ||
            PositionalWorth.IsNeedle(definition) ||
            definition.Behavior == "Charm_WhitePaper";

        /// <summary>
        /// 게임이 세는 방식으로 전체를 센다 - 정의의 카테고리에 자리 몫을 더한 것. 열쇠는 정의
        /// 카테고리 대신 행 카테고리만 내놓는다(<c>GetItemCategory()</c>를 덮어쓴다). 도구와 시험용이다.
        /// </summary>
        public static Dictionary<string, int> CountAll(IReadOnlyDictionary<GridPos, CharmSlot> byCell)
        {
            var counts = new Dictionary<string, int>();
            var positional = new List<string>();
            foreach (var pair in byCell)
            {
                var charm = pair.Value;
                if (charm.IsFiller) continue;

                if (!IsPositional(charm.Definition))
                {
                    foreach (var category in charm.Definition.Categories) Add(counts, category, 1);
                }

                positional.Clear();
                PositionalCategories(charm, pair.Key, byCell, positional);
                foreach (var category in positional) Add(counts, category, 1);
            }
            return counts;
        }

        /// <summary>
        /// 이 카테고리를 하나 더 모을 때 출발점이 되는 개수. 바탕 개수에, 평가 중인 배치에서 다른
        /// 자리 의존 아티팩트가 보태는 몫을 더한 것이다. <paramref name="charm"/> 자신의 몫은 뺀다.
        /// </summary>
        public static int CountFor(
            PlacementProblem problem, CharmSlot charm, string category,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors)
        {
            BaseCounts(problem).TryGetValue(category, out var count);
            if (neighbors is null) return count;

            List<string>? positional = null;
            foreach (var pair in neighbors)
            {
                var other = pair.Value;
                if (other == charm || other.IsFiller || !IsPositional(other.Definition)) continue;

                positional ??= new List<string>();
                positional.Clear();
                PositionalCategories(other, pair.Key, neighbors, positional);
                foreach (var contributed in positional)
                {
                    if (contributed == category) count++;
                }
            }
            return count;
        }

        /// <summary>그 자리들에 놓인 아티팩트를 게임 방식으로 센 것.</summary>
        internal static Dictionary<string, int> CountAt(PlacementProblem problem, IReadOnlyDictionary<int, GridPos> positions)
        {
            var byCell = new Dictionary<GridPos, CharmSlot>();
            foreach (var charm in problem.Charms)
            {
                if (positions.TryGetValue(charm.InstanceId, out var cell)) byCell[cell] = charm;
            }
            return CountAll(byCell);
        }

        /// <summary>
        /// 게임 수를 우리 셈의 차이만큼 옮긴다. 우리가 재현하지 않는 규칙(유니크 페어 등)은 게임 수에
        /// 남고 양쪽 셈에서 지워진다. 게임 수를 모르면 우리 셈을 그대로 쓴다.
        /// </summary>
        internal static Dictionary<string, int> Adjust(IReadOnlyDictionary<string, int>? reported,
            IReadOnlyDictionary<string, int> current, IReadOnlyDictionary<string, int> next)
        {
            var keys = new HashSet<string>(current.Keys);
            keys.UnionWith(next.Keys);
            if (reported is not null) keys.UnionWith(reported.Keys);

            var result = new Dictionary<string, int>();
            foreach (var key in keys)
            {
                current.TryGetValue(key, out var now);
                next.TryGetValue(key, out var then);
                var game = now;
                if (reported is not null) reported.TryGetValue(key, out game);
                result[key] = game - now + then;
            }
            return result;
        }

        /// <summary>게임이 세어 둔 지금 수. 조언 갈래에서는 갈래가 고쳐 둔 수다.</summary>
        internal static int Reported(PlacementProblem problem, string category) =>
            problem.ComboCounts is not null && problem.ComboCounts.TryGetValue(category, out var count) ? count : 0;

        /// <summary>
        /// 이 배치에서 게임이 셀 수. 게임 수에서 <b>지금 자리를 우리 방식으로 센 것</b>을 빼고 이
        /// 배치를 같은 방식으로 센 것을 더한다. 지금 배치에서는 정확히 게임 수가 되고, 우리가
        /// 재현하지 않는 규칙(유니크 페어 등)은 양쪽에서 지워진다. 판에 없던 아티팩트를 더한
        /// 후보 갈래는 그 몫이 더해지고, 뺀 갈래는 빠진다.
        /// </summary>
        internal static int CountOf(
            PlacementProblem problem, string category, IReadOnlyDictionary<GridPos, CharmSlot> byCell)
        {
            if (problem.CurrentComboCount is not { } current || problem.CurrentComboCategory != category)
            {
                var now = new Dictionary<GridPos, CharmSlot>();
                foreach (var charm in problem.Charms)
                {
                    if (problem.CurrentCharms.TryGetValue(charm.InstanceId, out var cell)) now[cell] = charm;
                }
                current = Count(now, category);
                problem.CurrentComboCount = current;
                problem.CurrentComboCategory = category;
            }
            return Reported(problem, category) - current + Count(byCell, category);
        }

        private static int Count(IReadOnlyDictionary<GridPos, CharmSlot> byCell, string category)
        {
            var count = 0;
            List<string>? positional = null;
            foreach (var pair in byCell)
            {
                var charm = pair.Value;
                if (charm.IsFiller) continue;

                if (!IsPositional(charm.Definition))
                {
                    if (charm.Definition.Categories.Contains(category)) count++;
                    continue;
                }

                positional ??= new List<string>();
                positional.Clear();
                PositionalCategories(charm, pair.Key, byCell, positional);
                foreach (var contributed in positional)
                {
                    if (contributed == category) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 게임 개수에서 자리 의존 아티팩트들이 지금 자리에서 보태고 있는 몫을 뺀 것. 문제마다 한 번만
        /// 짓는다 - 배정 비용 행렬의 최내곽에서 읽히는 값이다.
        /// </summary>
        private static Dictionary<string, int> BaseCounts(PlacementProblem problem)
        {
            if (problem.BaseComboCounts is not null) return problem.BaseComboCounts;

            var counts = new Dictionary<string, int>();
            if (problem.ComboCounts is not null)
            {
                foreach (var pair in problem.ComboCounts) counts[pair.Key] = pair.Value;
            }

            var byCell = new Dictionary<GridPos, CharmSlot>();
            foreach (var charm in problem.Charms)
            {
                if (problem.CurrentCharms.TryGetValue(charm.InstanceId, out var cell)) byCell[cell] = charm;
            }

            var positional = new List<string>();
            foreach (var pair in byCell)
            {
                if (pair.Value.IsFiller || !IsPositional(pair.Value.Definition)) continue;

                positional.Clear();
                if (pair.Value.Definition.Behavior == "Charm_WhitePaper" && pair.Value.ObservedCategories is not null)
                    positional.AddRange(pair.Value.ObservedCategories);
                else PositionalCategories(pair.Value, pair.Key, byCell, positional);
                foreach (var category in positional) Add(counts, category, -1);
            }

            // 게임 개수에 우리가 모르는 규칙(유니크 페어 등)이 섞여 있어도 음수로는 내려가지 않는다.
            foreach (var key in new List<string>(counts.Keys))
            {
                if (counts[key] < 0) counts[key] = 0;
            }

            problem.BaseComboCounts = counts;
            return counts;
        }

        private static void Add(Dictionary<string, int> counts, string category, int amount)
        {
            if (string.IsNullOrEmpty(category)) return;
            counts.TryGetValue(category, out var count);
            counts[category] = count + amount;
        }
    }
}
