using System;
using System.Collections.Generic;
using System.Linq;

namespace SephPlanner.Core.Combat
{
    internal static class CombatCoverage
    {
        private static readonly HashSet<string> Supported = new HashSet<string>((
            "PHYSICALDAMAGE FIREDAMAGE ICEDAMAGE LIGHTNINGDAMAGE WEAPONDAMAGEBONUS BASICATTACKDAMAGEBONUS " +
            "DASHATTACKDAMAGEBONUS SPECIALATTACKDAMAGEBONUS MPSKILLDAMAGE FINALWEAPONDAMAGE MAGICDAMAGEBONUS MAGICMP " +
            "FROSTRELICFLAME FROSTRELICDAMAGE FLAMESWORDFROST FLAMESWORDDAMAGE FLAMESWORDMAGICDAMAGE " +
            "CRITICAL CRITICALDAMAGEBONUS MAGICCRITICAL MAGICCRITICALDAMAGEBONUS WEAPONCRITICAL WEAPONCRITICALDAMAGE " +
            "WEAPONCRITICALDAMAGEAMPLIFY EXECUTION WEAPONDAMAGEBONUSBYDASHCOUNT DASHCOUNT ALLDAMAGEBONUS ELITEDAMAGE " +
            "IGNOREEVASION DEFENSETOATTACK DAMAGEREDUCTION IGNOREDEFENSE TRUEDAMAGE INFINITYMP @MAXMP FINALMP MPREGEN " +
            "MPREGENMULTIPLE MPRESONANCE CHARMDAMAGEBONUS FLAMESWORDCALLBACKFROST FLAMESWORDADDITIONALATTACK " +
            "FLAMESWORDADDITIONALATTACKFROMWEAPON FLAMESWORDADDITIONALATTACKFROMMAGIC FLAMESWORDMAX " +
            "COOLDOWNRECOVERYSPEED CHARGINGCHARMBONUS NOMAGICCOST MAGICCOSTREDUCE SPECIALATTACKSPEED ATTACKSPEED FIXEDATTACKSPEED"
        ).Split(' '), StringComparer.Ordinal);

        internal static IEnumerable<string> Missing(CombatStatState stats) => stats.Export()
            .SelectMany(source => source.Stats.Keys.Concat(source.Amplification.Keys)).Distinct(StringComparer.Ordinal)
            .Where(key => stats.Base(key) != 0 || stats.Amplification(key) != 0)
            .Where(key => !Supports(key))
            .Select(key => "능력치의 전투 동작 미반영: " + key);

        internal static bool Supports(string key) => Supported.Contains(key) || IsConversion(key);

        private static bool IsConversion(string key)
        {
            var elements = new[] { "PHYSICAL", "FIRE", "ICE", "LIGHTNING" };
            return elements.Any(from => elements.Any(to => from != to && key == from + "TO" + to));
        }
    }
}
