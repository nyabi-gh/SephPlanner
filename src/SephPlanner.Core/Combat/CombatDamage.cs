using System;
using System.Collections.Generic;
using System.Linq;

namespace SephPlanner.Core.Combat
{
    public static class CombatDamage
    {
        private static readonly string[] Elements = { "FIREDAMAGE", "ICEDAMAGE", "LIGHTNINGDAMAGE", "PHYSICALDAMAGE" };
        public static double At(IReadOnlyList<double> values, int level, double fallback = 0) =>
            values.Count == 0 ? fallback : values[Math.Min(Math.Max(0, level), values.Count - 1)];

        public static int At(IReadOnlyList<int> values, int level, int fallback = 0) =>
            values.Count == 0 ? fallback : values[Math.Min(Math.Max(0, level), values.Count - 1)];

        public static double Base(CombatAttack attack, int level, CombatStatState stats, int manaCost, double support = 1)
        {
            var stat = attack.Stat;
            if (attack.DamageKind == CombatDamageKind.IceRelic && stats.Read("FROSTRELICFLAME") > 0) stat = "FIREDAMAGE";
            if (attack.DamageKind == CombatDamageKind.FlameSword)
                stat = stats.Read("FLAMESWORDFROST") > 0 ? "ICEDAMAGE" : "FIREDAMAGE";
            var damage = At(attack.BaseDamage, level) + RelatedStat(stat, stats) * At(attack.StatPercent, level, 100) / 100;
            switch (attack.DamageKind)
            {
                case CombatDamageKind.Weapon:
                    damage *= Factor(stats.Read("WEAPONDAMAGEBONUS"));
                    damage *= Factor(stats.Read(attack.Action == CombatActionKind.Dash ? "DASHATTACKDAMAGEBONUS" :
                        attack.Action == CombatActionKind.Special ? "SPECIALATTACKDAMAGEBONUS" : "BASICATTACKDAMAGEBONUS"));
                    if (manaCost > 0) damage *= Factor(stats.Read("MPSKILLDAMAGE"));
                    damage *= Factor(stats.Read("FINALWEAPONDAMAGE"));
                    break;
                case CombatDamageKind.Bolt:
                    damage *= Factor(stats.Read("MAGICDAMAGEBONUS"));
                    damage *= Factor(stats.Read("MPSKILLDAMAGE"));
                    if (manaCost > 0) damage *= Factor(manaCost / 10d * stats.Read("MAGICMP"));
                    if (attack.BasicDamageBonus) damage *= Factor(stats.Read("BASICATTACKDAMAGEBONUS"));
                    break;
                case CombatDamageKind.IceRelic:
                    damage *= Factor(stats.Read("FROSTRELICDAMAGE"));
                    break;
                case CombatDamageKind.FlameSword:
                    damage *= Factor(stats.Read("FLAMESWORDDAMAGE"));
                    if (stats.Read("FLAMESWORDMAGICDAMAGE") > 0) damage *= Factor(stats.Read("MAGICDAMAGEBONUS"));
                    break;
            }
            return damage * attack.Multiplier * support;
        }

        public static double ExpectedHit(CombatAttack attack, double damage, CombatStatState stats, CombatScenario scenario)
        {
            if (!Finite(damage)) throw new ArithmeticException("피해 계산이 유한한 값이 아닙니다.");
            if (damage <= 0) return 0;
            var target = scenario.TargetStats;
            var weapon = attack.DamageKind == CombatDamageKind.Weapon;
            var magic = attack.UsesMagicCritical;
            var chance = stats.Read("CRITICAL") / 100d;
            var criticalBonus = stats.Read("CRITICALDAMAGEBONUS") + 50;
            if (magic)
            {
                chance += stats.Read("MAGICCRITICAL") / 100d;
                criticalBonus += stats.Read("MAGICCRITICALDAMAGEBONUS");
            }
            else if (weapon)
            {
                chance += stats.Read("WEAPONCRITICAL") / 100d;
                criticalBonus += stats.Read("WEAPONCRITICALDAMAGE");
                if (stats.Read("WEAPONCRITICALDAMAGEAMPLIFY") > 0)
                    criticalBonus += CombatStatMath.Truncate((float)unchecked(criticalBonus * stats.Read("WEAPONCRITICALDAMAGEAMPLIFY")) / 100f);
            }
            chance *= 1 - Clamp(target.GetValueOrDefault("CRITICALRESIST"), -100, 100) / 100;
            var execution = stats.Read("EXECUTION") > 0 ? Clamp((chance - 100) / 100, 0, 1) : 0;
            var critical = (1 - execution) * Clamp(chance / 100, 0, 1);
            var ordinary = 1 - execution - critical;
            var value = damage;
            if (weapon) value *= Factor(stats.Read("WEAPONDAMAGEBONUSBYDASHCOUNT") * (double)stats.Read("DASHCOUNT"));
            value += damage * (stats.Read("ALLDAMAGEBONUS") + (scenario.Boss ? stats.Read("ELITEDAMAGE") : 0)) / 100;
            var element = attack.Element;
            if (attack.DamageKind == CombatDamageKind.IceRelic)
                element = stats.Read("FROSTRELICFLAME") > 0 ? "FIRE" : "ICE";
            if (attack.DamageKind == CombatDamageKind.FlameSword)
                element = stats.Read("FLAMESWORDFROST") > 0 ? "ICE" : "FIRE";
            var result = ordinary * Applied(value, element, stats, target) +
                         critical * Applied(value * Factor(criticalBonus), element, stats, target) +
                         execution * Applied(value * Factor(criticalBonus * 2d), element, stats, target);
            var evasion = Math.Min(10000, target.GetValueOrDefault("EVASION"));
            var evade = 100 * Math.Log(evasion / 6200d + 1) * 0.8 + target.GetValueOrDefault("ABSOLUTEEVASION");
            evade = Math.Max(0, evade);
            if (stats.Read("IGNOREEVASION") > 0) evade *= 1 - stats.Read("IGNOREEVASION") / 100d;
            if (target.GetValueOrDefault("EVASIONDISABLE") > 0) evade = 0;
            return result * (1 - Clamp(evade / 100, 0, 1));
        }

        public static double RelatedStat(string formula, CombatStatState stats)
        {
            var elements = Elements;
            if (formula == "HIGHEST") return elements.Max(stats.Read);
            if (formula == "LOWEST") return elements.Min(stats.Read);
            if (formula == "AVERAGEALL") return elements.Sum(stats.Read) / 4;
            if (formula.StartsWith("AVERAGE/", StringComparison.Ordinal))
            {
                var keys = formula.Substring(8).Split(',').Where(elements.Contains).ToArray();
                if (keys.Length == 0) throw new InvalidOperationException("평균 피해의 원본 능력치가 없습니다.");
                return keys.Average(stats.Read);
            }
            return formula.Length == 0 ? 0 : stats.Read(formula);
        }

        private static double Applied(double damage, string element, CombatStatState stats, Dictionary<string, int> target)
        {
            if (stats.Read("DEFENSETOATTACK") > 0) damage *= 1 + DefenseReduction(1, stats.Read("DAMAGEREDUCTION"));
            damage *= 1 - Clamp(target.GetValueOrDefault("RECEIVEDDAMAGEREDUCTION") / 100d, 0, 1);
            damage *= Factor(Math.Max(0, target.GetValueOrDefault("RECEIVEDDAMAGEINCREASE")));
            damage *= 1 - Clamp(target.GetValueOrDefault(element + "DEFENSE"), 0, 99) / 100;
            var defense = target.GetValueOrDefault("DEFENSETOATTACK") > 0 ? 0 : target.GetValueOrDefault("DAMAGEREDUCTION");
            var reduction = Math.Min(damage, DefenseReduction(damage, defense));
            if (stats.Read("IGNOREDEFENSE") > 0) reduction *= 1 - stats.Read("IGNOREDEFENSE") / 100d;
            damage -= reduction;
            damage += stats.Read("TRUEDAMAGE") - target.GetValueOrDefault("TOUGHNESS");
            return CombatStatMath.Truncate(Math.Max(1, damage));
        }

        private static double DefenseReduction(double damage, int defense) =>
            defense > 0 ? damage * Math.Log(defense / 40d + 1) * 0.445 : damage * defense / 100;

        internal static double Factor(double percent) => 1 + percent / 100;
        internal static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
