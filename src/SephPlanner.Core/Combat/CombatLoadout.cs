using System;
using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Combat
{
    public sealed class CombatAttackInstance
    {
        public CombatAttack Attack { get; set; } = new CombatAttack();
        public int InstanceId { get; set; }
        public int DefinitionId { get; set; }
        public int Level { get; set; }
        public double SupportPercent { get; set; }
        public double CooldownBonus { get; set; }
        public double CostBonus { get; set; }
    }

    public sealed class CombatLoadout
    {
        public CombatStatState Stats { get; set; } = new CombatStatState();
        public List<CombatAttackInstance> Attacks { get; set; } = new List<CombatAttackInstance>();
        public List<string> Unsupported { get; set; } = new List<string>();
    }

    public sealed class LocatedCombatCharm
    {
        public CharmSlot Charm { get; set; } = new CharmSlot();
        public GridPos Position { get; set; }
        public int Level { get; set; }
        public bool Active { get; set; }
    }

    public sealed class PlacementCombatContext
    {
        internal System.Threading.CancellationToken Cancellation { get; set; }
        public CombatSnapshot Snapshot { get; set; } = new CombatSnapshot();
        public CombatScenario Scenario { get; set; } = new CombatScenario();
        public CombatStatState Background { get; set; } = new CombatStatState();
        public List<LocatedCombatCharm> Current { get; set; } = new List<LocatedCombatCharm>();
        public Dictionary<string, int> ExtraComboCounts { get; set; } = new Dictionary<string, int>();
    }

    public static class CombatLoadouts
    {
        public static CombatLoadout Build(PlacementProblem problem, PlacementCombatContext context, IReadOnlyList<LocatedCombatCharm> placed, bool details = true)
        {
            var loadout = new CombatLoadout { Stats = context.Background.Copy() };
            if (details) loadout.Unsupported.AddRange(context.Snapshot.Unsupported);
            foreach (var attack in context.Snapshot.WeaponAttacks)
                loadout.Attacks.Add(new CombatAttackInstance { Attack = attack });
            var byCell = placed.ToDictionary(item => item.Position, item => item.Charm);
            var byId = placed.ToDictionary(item => item.Charm.InstanceId);
            foreach (var item in placed)
            {
                var charm = item.Charm;
                if (charm.IsFiller || !item.Active) continue;
                var definition = charm.Definition;
                var level = Math.Min(definition.MaxLevel, item.Level);
                var source = Source("charm:" + charm.InstanceId, definition.Combat, level);
                if (definition.Combat.FireIcePosition)
                    Add(source.Stats, HorizontalStatBonus.IsLeft(item.Position) ? "FROSTRELICFLAME" : "FLAMESWORDFROST", 1);
                if (definition.HorizontalStats is { } horizontal)
                {
                    var left = HorizontalStatBonus.IsLeft(item.Position);
                    Add(source.Stats, horizontal.LeftStat, CombatDamage.At(left ? horizontal.MainByLevel : horizontal.OppositeByLevel, level));
                    Add(source.Stats, horizontal.RightStat, CombatDamage.At(left ? horizontal.OppositeByLevel : horizontal.MainByLevel, level));
                }
                foreach (var bonus in definition.ContextStats)
                {
                    if (bonus.CombatKey.Length == 0) continue;
                    var count = bonus.Source == StatCountSource.StoneTablets ? problem.Tablets.Count :
                        bonus.Source == StatCountSource.RowCategory ?
                            definition.LineCategories.Count > 0 && PositionalWorth.LineCategory(definition, item.Position) == bonus.Category ? 1 : 0 :
                        placed.Count(other => !other.Charm.IsFiller && problem.Grid.ToIndex(other.Position.X, other.Position.Y) < bonus.SlotCount);
                    Add(bonus.CombatAmplification ? source.Amplification : source.Stats, bonus.CombatKey,
                        CombatStatMath.Truncate(CombatDamage.At(bonus.AmountByLevel, level) * count));
                }
                if (definition.NeighborLevelBonus.Count > 0)
                {
                    var sum = placed.Where(other => other != item && !other.Charm.IsFiller &&
                        Math.Abs(other.Position.X - item.Position.X) <= 1 && Math.Abs(other.Position.Y - item.Position.Y) <= 1)
                        .Sum(other => Math.Min(other.Level, other.Charm.Definition.MaxLevel));
                    Add(source.Stats, "ALLDAMAGEBONUS", CombatStatMath.Truncate(Math.Floor(
                        CombatDamage.At(definition.NeighborLevelBonus, level) * sum)));
                }
                loadout.Stats.SetSource(source);
                AddEffect(loadout, definition.Combat, level, charm.InstanceId, definition.EntityId, details: details);
                if (details && !definition.Combat.Collected) loadout.Unsupported.Add(Name(charm) + ": 전투 효과를 수집하지 않았습니다.");
                if (details && definition.Behavior == "Charm_WhitePaper") loadout.Unsupported.Add("하얀 종이 중첩의 이동·갱신 순서는 최근 관측값에 따른 추정입니다.");
            }
            var counts = ComboCounting.CountAll(byCell);
            foreach (var extra in context.ExtraComboCounts) Add(counts, extra.Key, extra.Value);
            foreach (var pair in counts)
            {
                var combo = problem.Combos?.Invoke(pair.Key);
                if (pair.Value <= 0) continue;
                if (combo == null)
                {
                    if (details) loadout.Unsupported.Add("콤보 " + pair.Key + ": 전투 자료가 없습니다.");
                    continue;
                }
                loadout.Stats.SetSource(Source("combo:" + pair.Key, combo.Combat, 0, pair.Value));
                AddEffect(loadout, combo.Combat, 0, 0, 0, pair.Value, details);
                if (details && !combo.Combat.Collected) loadout.Unsupported.Add("콤보 " + pair.Key + ": 전투 효과를 수집하지 않았습니다.");
            }
            foreach (var item in placed)
            {
                var definition = item.Charm.Definition;
                if (PositionalWorth.IsNeedle(definition) &&
                    PositionalWorth.DependencyTarget(item.Charm, item.Position, byCell, out var target, out _) &&
                    byId.TryGetValue(target.InstanceId, out var targetItem) && targetItem.Active)
                {
                    var level = item.Active ? Math.Min(item.Level, definition.MaxLevel) : 0;
                    var amount = CombatDamage.At(definition.DependencyBonusByLevel, level);
                    if (definition.HasDependencyCondition && target.Definition.Rarity <= definition.DependencyMaxRarity)
                        amount += CombatDamage.At(definition.DependencyExtraByLevel, level);
                    foreach (var attack in loadout.Attacks.Where(attack => attack.InstanceId == target.InstanceId))
                        attack.SupportPercent += amount;
                }
                if (!item.Active || definition.MagicSupport is not { } support ||
                    !byCell.TryGetValue(item.Position.Offset(support.OffsetX, support.OffsetY), out var magic) ||
                    !magic.Definition.IsMagic || !byId[magic.InstanceId].Active) continue;
                foreach (var attack in loadout.Attacks.Where(attack => attack.InstanceId == magic.InstanceId))
                {
                    var amount = CombatDamage.At(support.AmountByLevel, Math.Min(item.Level, definition.MaxLevel));
                    if (support.Effect == MagicSupportEffect.ManaCostReduction) attack.CostBonus -= amount;
                    else attack.CooldownBonus += amount;
                }
            }
            if (details) loadout.Unsupported = loadout.Unsupported.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList();
            return loadout;
        }

        internal static CombatStatSource Source(string id, CharmCombatEffect effect, int level, int count = int.MaxValue)
        {
            var source = new CombatStatSource { Id = id };
            foreach (var grant in effect.Stats)
                if (grant.Threshold <= count)
                    Add(grant.Amplification ? source.Amplification : source.Stats, grant.Key, CombatDamage.At(grant.Values, level));
            return source;
        }

        private static void AddEffect(CombatLoadout loadout, CharmCombatEffect effect, int level, int instance, int definition, int count = int.MaxValue, bool details = true)
        {
            foreach (var attack in effect.Attacks)
                if (attack.Threshold <= count)
                    loadout.Attacks.Add(new CombatAttackInstance { Attack = attack, Level = level, InstanceId = instance, DefinitionId = definition });
            if (details) loadout.Unsupported.AddRange(effect.Unsupported.Select(message => instance == 0 ? message : "아티팩트 " + definition + ": " + message));
        }

        internal static void Add(Dictionary<string, int> values, string key, int amount) =>
            values[key] = unchecked(values.GetValueOrDefault(key) + amount);

        private static string Name(CharmSlot charm) => charm.Definition.Names.TryGetValue("current", out var name) ? name : charm.Definition.Id;
    }
}
