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
            var related = attack.DamageKind == CombatDamageKind.Weapon ? WeaponStat(stat, attack.Element, stats, out _) : RelatedStat(stat, stats);
            var damage = At(attack.BaseDamage, level) + related * At(attack.StatPercent, level, 100) / 100;
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
            if (weapon && attack.ElementFromRelatedStat) WeaponStat(attack.Stat, element, stats, out element);
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

        private static double WeaponStat(string formula, string element, CombatStatState stats, out string resolvedElement)
        {
            resolvedElement = element;
            if (formula.Length == 0) return Elements.Contains(element + "DAMAGE") ? stats.Read(element + "DAMAGE") : 1;
            if (Elements.Contains(formula))
            {
                resolvedElement = formula.Substring(0, formula.Length - "DAMAGE".Length);
                return stats.Read(formula);
            }
            var parts = formula.Split('/');
            if (parts[0] == "HIGHEST" || parts[0] == "LOWEST")
            {
                var value = RelatedStat(parts[0], stats);
                var key = Elements.First(candidate => stats.Read(candidate) == value);
                resolvedElement = key.Substring(0, key.Length - "DAMAGE".Length);
                return value;
            }
            if (parts[0] == "AVERAGEALL") return RelatedStat(parts[0], stats);
            if (parts[0] == "AVERAGE" && parts.Length > 1) return RelatedStat("AVERAGE/" + parts[1], stats);
            // WeaponSimple.GetRelatedStatMultiplier는 일반·혼돈 속성과 알 수 없는 식의 배수를 1로 둔다.
            resolvedElement = "NORMAL";
            return 1;
        }

        private static double Applied(double damage, string element, CombatStatState stats, Dictionary<string, int> target)
        {
            // UnitAvatar.ApplyDamage와 같은 float 덧셈·뺄셈 순서를 지켜 정수 피해 경계가 달라지지 않게 한다.
            var value = (float)damage;
            if (stats.Read("DEFENSETOATTACK") > 0) value += value * DefenseReduction(1, stats.Read("DAMAGEREDUCTION"));
            value -= value * (float)Clamp(target.GetValueOrDefault("RECEIVEDDAMAGEREDUCTION") / 100f, 0, 1);
            value += value * (Math.Max(0, target.GetValueOrDefault("RECEIVEDDAMAGEINCREASE")) / 100f);
            value -= value * (Resistance(element, target) / 100f);
            var defense = target.GetValueOrDefault("DEFENSETOATTACK") > 0 ? 0 : target.GetValueOrDefault("DAMAGEREDUCTION");
            var reduction = Math.Min(value, DefenseReduction(value, defense));
            if (stats.Read("IGNOREDEFENSE") > 0) reduction *= 1 - stats.Read("IGNOREDEFENSE") / 100f;
            value -= reduction;
            value += stats.Read("TRUEDAMAGE");
            value -= target.GetValueOrDefault("TOUGHNESS");
            return CombatStatMath.Truncate(Math.Max(1, value));
        }

        private static int Resistance(string element, Dictionary<string, int> target)
        {
            var fire = target.GetValueOrDefault("FIREDEFENSE");
            var ice = target.GetValueOrDefault("ICEDEFENSE");
            var lightning = target.GetValueOrDefault("LIGHTNINGDEFENSE");
            var physical = target.GetValueOrDefault("PHYSICALDEFENSE");
            var value = element switch
            {
                "FIRE" => fire,
                "ICE" => ice,
                "LIGHTNING" => lightning,
                "PHYSICAL" => physical,
                "FIREANDICE" => Math.Max(fire, ice),
                "FIREANDLIGHTNING" => Math.Max(fire, lightning),
                "ICEANDLIGHTNING" => Math.Max(ice, lightning),
                "CHAOS" => Math.Max(Math.Max(fire, ice), Math.Max(lightning, physical)),
                _ => 0,
            };
            return Math.Min(99, Math.Max(0, value));
        }

        private static float DefenseReduction(float damage, int defense) =>
            defense > 0 ? damage * (float)Math.Log(defense / 40f + 1f) * 0.445f : damage * defense / 100f;

        internal static double Factor(double percent) => 1 + percent / 100;
        internal static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
