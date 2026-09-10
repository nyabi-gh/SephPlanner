using System.Collections.Generic;

namespace SephPlanner.Core.Combat
{
    public enum CombatActionKind { Basic, Dash, Special, Magic, Automatic }
    public enum CombatDamageKind { Weapon, Bolt, IceRelic, FlameSword, Physical, Elemental }
    public enum CombatTrigger { Interval, Dash, AttackHit }

    public sealed class CombatScenario
    {
        public double DurationSeconds { get; set; } = 30;
        public int TargetCount { get; set; } = 1;
        // 첫 대상 외의 적중 비율은 실측 전 비교용 추정값이다.
        public double AdditionalTargetFraction { get; set; } = 0.5;
        public double ComparisonWindowSeconds { get; set; } = 5;
        public bool Boss { get; set; }
        public Dictionary<string, int> TargetStats { get; set; } = new Dictionary<string, int>();
        public double InitialManaFraction { get; set; } = 1;
        public double InitialChargeFraction { get; set; } = 1;
        public bool PreserveActivation { get; set; } = true;
        public bool PrioritizeBuild { get; set; }
        public bool AllowUnsupportedChanges { get; set; }
        public List<CombatActionKind> WeaponSequence { get; set; } = new List<CombatActionKind> { CombatActionKind.Basic };
        public List<int> MagicPriority { get; set; } = new List<int>();
        public List<int> DisabledMagic { get; set; } = new List<int>();
        public bool UseMagic { get; set; } = true;
        public string MeasuredWeaponKey { get; set; } = "";
        public Dictionary<string, double> MeasuredActionSeconds { get; set; } = new Dictionary<string, double>();

        // 동작 시간을 수집하지 못했을 때의 비교 시나리오 추정값이며 게임의 공격 주기가 아니다.
        public double BasicSeconds { get; set; } = 1;
        public double DashSeconds { get; set; } = 1;
        public double SpecialSeconds { get; set; } = 1;
        public double CastingSeconds { get; set; } = 1;

        public CombatScenario Copy()
        {
            var copy = (CombatScenario)MemberwiseClone();
            copy.TargetStats = new Dictionary<string, int>(TargetStats);
            copy.WeaponSequence = new List<CombatActionKind>(WeaponSequence);
            copy.MagicPriority = new List<int>(MagicPriority);
            copy.DisabledMagic = new List<int>(DisabledMagic);
            copy.MeasuredActionSeconds = new Dictionary<string, double>(MeasuredActionSeconds);
            return copy;
        }
    }

    public sealed class CombatStatGrant
    {
        public string Key { get; set; } = "";
        public bool Amplification { get; set; }
        public List<int> Values { get; set; } = new List<int>();
        public int Threshold { get; set; }
    }

    public sealed class CombatAttack
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public CombatActionKind Action { get; set; }
        public CombatDamageKind DamageKind { get; set; }
        public CombatTrigger Trigger { get; set; }
        public int Threshold { get; set; }
        public string Stat { get; set; } = "PHYSICALDAMAGE";
        public string Element { get; set; } = "PHYSICAL";
        public List<double> BaseDamage { get; set; } = new List<double>();
        public List<double> StatPercent { get; set; } = new List<double>();
        public List<double> IntervalByLevel { get; set; } = new List<double>();
        public List<int> ManaCost { get; set; } = new List<int>();
        public double Multiplier { get; set; } = 1;
        public double Hits { get; set; } = 1;
        public List<int> HitsByLevel { get; set; } = new List<int>();
        public int MaxTargets { get; set; } = 1;
        public double DurationSeconds { get; set; }
        public double CooldownSeconds { get; set; }
        public double TriggerCooldownSeconds { get; set; }
        public bool ParallelRecharge { get; set; }
        public bool Recharges { get; set; } = true;
        public int Charges { get; set; } = 1;
        public bool BasicDamageBonus { get; set; }
        public bool UsesMagicCritical { get; set; }
        public bool SpecialUsesAttackSpeed { get; set; }
        public double AttackSpeedAmplification { get; set; }
        public bool RequiresPaidDash { get; set; }
        public bool ChargingCharm { get; set; }
        public List<string> Unsupported { get; set; } = new List<string>();
    }

    public sealed class CharmCombatEffect
    {
        public List<CombatStatGrant> Stats { get; set; } = new List<CombatStatGrant>();
        public List<CombatAttack> Attacks { get; set; } = new List<CombatAttack>();
        public List<string> Unsupported { get; set; } = new List<string>();
        public bool FireIcePosition { get; set; }
        public bool Collected { get; set; }
    }

    public sealed class CombatSnapshot
    {
        public Dictionary<string, int> ObservedStats { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> ObservedAmplification { get; set; } = new Dictionary<string, int>();
        public List<CombatAttack> WeaponAttacks { get; set; } = new List<CombatAttack>();
        public List<string> Unsupported { get; set; } = new List<string>();
        public double ManaRecoveryDelay { get; set; }
        public double GlobalMagicCooldown { get; set; }
        public int InfinityMana { get; set; }
        public int Gold { get; set; }
        public Dictionary<string, int> Constants { get; set; } = new Dictionary<string, int>();
    }

    public sealed class CombatContribution
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double Damage { get; set; }
        public int Uses { get; set; }
    }

    public sealed class CombatResult
    {
        public int TargetCount { get; set; }
        public double AdditionalTargetFraction { get; set; }
        public double ComparisonWindowSeconds { get; set; }
        public double OpeningDamage { get; set; }
        public double EndingDamage { get; set; }
        public double OpeningDps => ComparisonWindowSeconds > 0 ? OpeningDamage / ComparisonWindowSeconds : 0;
        public double EndingDps => ComparisonWindowSeconds > 0 ? EndingDamage / ComparisonWindowSeconds : 0;
        public double? EmptyStartDps { get; set; }
        public Dictionary<string, int> FinalStats { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> FinalAmplification { get; set; } = new Dictionary<string, int>();
        public double DurationSeconds { get; set; }
        public double TotalDamage { get; set; }
        public double Dps => DurationSeconds > 0 ? TotalDamage / DurationSeconds : 0;
        public double RemainingMana { get; set; }
        public List<CombatContribution> Contributions { get; set; } = new List<CombatContribution>();
        public List<string> Unsupported { get; set; } = new List<string>();
    }
}
