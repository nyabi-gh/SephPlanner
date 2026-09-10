using System;
using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Combat
{
    public static class CombatChangeAssessment
    {
        public static List<string> Compare(PlacementProblem problem, Arrangement current, Arrangement proposed)
        {
            if (problem.Combat == null) return new List<string>();
            var before = CombatPlanning.Locate(problem, current);
            var after = CombatPlanning.Locate(problem, proposed);
            var changes = new List<string>();
            foreach (var charm in problem.Charms.Where(charm => !charm.IsFiller))
            {
                var oldItem = before.FirstOrDefault(item => item.Charm.InstanceId == charm.InstanceId);
                var newItem = after.FirstOrDefault(item => item.Charm.InstanceId == charm.InstanceId);
                if (oldItem?.Active != true && newItem?.Active != true) continue;
                changes.AddRange(HealthChanges(charm, oldItem, newItem));
                if (!Missing(charm.Definition)) continue;
                if (State(oldItem) != State(newItem))
                    changes.Add(Name(charm) + ": " + ChangedState(oldItem, newItem) + ". 미지원 효과에 미치는 영향은 계산하지 못합니다.");
                else if (Connections(oldItem!, before) != Connections(newItem!, after))
                    changes.Add(Name(charm) + ": 미지원 효과와 관련된 주변 배치·지원 연결이 달라집니다.");
            }

            var oldCounts = Counts(before);
            var newCounts = Counts(after);
            foreach (var category in oldCounts.Keys.Union(newCounts.Keys).OrderBy(value => value, StringComparer.Ordinal))
            {
                var oldCount = oldCounts.GetValueOrDefault(category);
                var newCount = newCounts.GetValueOrDefault(category);
                if (oldCount == newCount) continue;
                var combo = problem.Combos?.Invoke(category);
                if (combo != null && !Missing(combo.Combat)) continue;
                changes.Add("콤보 " + (combo == null ? category : Naming.Of(combo.Names, combo.Id, category)) +
                    $": 미지원 효과의 콤보 수량이 {oldCount} → {newCount}로 달라집니다.");
            }
            return changes;

            Dictionary<string, int> Counts(List<LocatedCombatCharm> items)
            {
                var counts = ComboCounting.CountAll(items.ToDictionary(item => item.Position, item => item.Charm));
                foreach (var pair in problem.Combat.ExtraComboCounts) CombatLoadouts.Add(counts, pair.Key, pair.Value);
                return counts;
            }
        }

        private static bool Missing(CharmDefinition definition) => Missing(definition.Combat, false) ||
            definition.HorizontalStats is { } horizontal &&
                (!CombatCoverage.Supports(horizontal.LeftStat) || !CombatCoverage.Supports(horizontal.RightStat)) ||
            definition.Behavior == "Charm_WhitePaper" || definition.ContextStats.Any(bonus =>
                bonus.CombatKey.Length == 0 || !CombatCoverage.Supports(bonus.CombatKey));

        private static bool Missing(CharmCombatEffect effect, bool includeHealth = true) => !effect.Collected || effect.Unsupported.Count > 0 ||
            effect.Attacks.Any(attack => attack.Unsupported.Count > 0) ||
            effect.Stats.Any(grant => !CombatCoverage.Supports(grant.Key) &&
                (includeHealth || !HealthGrant(grant)) && grant.Values.Any(value => value != 0));

        private static bool HealthGrant(CombatStatGrant grant) => !grant.Amplification && (grant.Key == "@MAXHP" || grant.Key == "@FINALHP");

        private static IEnumerable<string> HealthChanges(CharmSlot charm, LocatedCombatCharm? before, LocatedCombatCharm? after)
        {
            foreach (var grants in charm.Definition.Combat.Stats.Where(HealthGrant).GroupBy(grant => grant.Key))
            {
                var oldValue = Value(before);
                var newValue = Value(after);
                if (oldValue == newValue) continue;
                var percent = grants.Key == "@FINALHP";
                yield return Name(charm) + ": " + (percent ? "최대 체력 증감률" : "최대 체력 가산값") + " " +
                    oldValue.ToString("+0;-0;0") + (percent ? "%" : "") + " → " + newValue.ToString("+0;-0;0") + (percent ? "%" : "") +
                    ". DPS에 포함하지 않는 체력 효과 변경이므로 자동 적용 전 확인이 필요합니다.";

                int Value(LocatedCombatCharm? item) => item?.Active == true
                    ? grants.Sum(grant => CombatDamage.At(grant.Values, Math.Min(item.Level, charm.Definition.MaxLevel))) : 0;
            }
        }

        private static string ChangedState(LocatedCombatCharm? before, LocatedCombatCharm? after)
        {
            if (before == null) return "추천 배치에 추가됩니다";
            if (after == null) return "추천 배치에서 제외됩니다";
            var parts = new List<string>();
            if (before.Position != after.Position) parts.Add($"위치 ({before.Position.X + 1},{before.Position.Y + 1}) → ({after.Position.X + 1},{after.Position.Y + 1})");
            var oldLevel = Math.Min(before.Level, before.Charm.Definition.MaxLevel);
            var newLevel = Math.Min(after.Level, after.Charm.Definition.MaxLevel);
            if (oldLevel != newLevel) parts.Add($"유효 레벨 {oldLevel} → {newLevel}");
            if (before.Active != after.Active) parts.Add(before.Active ? "활성 → 비활성" : "비활성 → 활성");
            return string.Join(" · ", parts);
        }

        private static string State(LocatedCombatCharm? item) => item == null ? "없음" : FormattableString.Invariant(
            $"{item.Charm.InstanceId}:{item.Position.X},{item.Position.Y}:{Math.Min(item.Level, item.Charm.Definition.MaxLevel)}:{item.Active}");

        private static string Connections(LocatedCombatCharm item, List<LocatedCombatCharm> items)
        {
            var byCell = items.ToDictionary(other => other.Position, other => other.Charm);
            var related = new HashSet<int> { item.Charm.InstanceId };
            // 미지원 고유 효과의 정확한 의존 범위를 모르므로 주변 8칸 변화도 보수적으로 알린다.
            foreach (var other in items)
                if (Math.Abs(other.Position.X - item.Position.X) <= 1 && Math.Abs(other.Position.Y - item.Position.Y) <= 1)
                    related.Add(other.Charm.InstanceId);
            var edges = new List<string>();
            foreach (var helper in items)
            {
                CharmSlot? target = null;
                if (PositionalWorth.IsNeedle(helper.Charm.Definition) &&
                    PositionalWorth.DependencyTarget(helper.Charm, helper.Position, byCell, out var needleTarget, out _))
                    target = needleTarget;
                else if (helper.Charm.Definition.MagicSupport is { } support &&
                    byCell.TryGetValue(helper.Position.Offset(support.OffsetX, support.OffsetY), out var magic) && magic.Definition.IsMagic)
                    target = magic;
                if (target == null || helper.Charm.InstanceId != item.Charm.InstanceId && target.InstanceId != item.Charm.InstanceId) continue;
                related.Add(helper.Charm.InstanceId);
                related.Add(target.InstanceId);
                edges.Add(helper.Charm.InstanceId + ">" + target.InstanceId);
            }
            return string.Join(";", items.Where(other => related.Contains(other.Charm.InstanceId))
                .OrderBy(other => other.Charm.InstanceId).Select(State)) + "|" + string.Join(";", edges.OrderBy(edge => edge, StringComparer.Ordinal));
        }

        private static string Name(CharmSlot charm) => Naming.Of(charm.Definition.Names, charm.Definition.Id, "아티팩트");
    }
}
