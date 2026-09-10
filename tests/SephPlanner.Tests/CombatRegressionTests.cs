using SephPlanner.Core.Combat;

namespace SephPlanner.Tests;

public sealed class CombatRegressionTests
{
    [Theory]
    [InlineData("", "NORMAL", 1)]
    [InlineData("", "CHAOS", 1)]
    [InlineData("", "FIRE", 200)]
    [InlineData("UNKNOWN", "FIRE", 1)]
    [InlineData("HIGHEST/PHYSICALDAMAGE", "PHYSICAL", 200)]
    [InlineData("LOWEST/PHYSICALDAMAGE", "PHYSICAL", 40)]
    [InlineData("AVERAGEALL/", "PHYSICAL", 100)]
    public void WeaponStatFormulasFollowTheGameFallbacks(string formula, string element, double expected)
    {
        var stats = new CombatStatState();
        stats.SetSource(new()
        {
            Id = "weapon",
            Stats = { ["FIREDAMAGE"] = 200, ["ICEDAMAGE"] = 40, ["LIGHTNINGDAMAGE"] = 60, ["PHYSICALDAMAGE"] = 100 }
        });
        Assert.Equal(expected, CombatDamage.Base(new() { DamageKind = CombatDamageKind.Weapon, Stat = formula, Element = element }, 0, stats, 0));
    }

    [Theory]
    [InlineData(CombatActionKind.Magic, 0, 0.1)]
    [InlineData(CombatActionKind.Magic, 0.1, 0)]
    [InlineData(CombatActionKind.Automatic, 0.1, 0)]
    public void FractionalCooldownsAndAutomaticPeriodsExcludeTheFightEnd(CombatActionKind kind, double cooldown, double globalCooldown)
    {
        var loadout = new CombatLoadout();
        loadout.Attacks.Add(new()
        {
            Attack = new()
            {
                Action = kind,
                DamageKind = CombatDamageKind.Bolt,
                CooldownSeconds = cooldown,
                IntervalByLevel = new() { cooldown },
                BaseDamage = new() { 100 },
                StatPercent = new() { 0 },
            }
        });
        var result = CombatSimulator.Evaluate(loadout, new() { GlobalMagicCooldown = globalCooldown }, new()
        {
            DurationSeconds = 1,
            CastingSeconds = 0.01,
            WeaponSequence = new(),
        });
        Assert.Equal(10, Assert.Single(result.Contributions).Uses);
    }

    [Fact]
    public void ExtraAutomaticEventsDoNotChangeTheManaBudget()
    {
        var loadout = new CombatLoadout();
        loadout.Stats.SetSource(new() { Id = "mana", Stats = { ["@MAXMP"] = 100, ["MPREGEN"] = 3 } });
        loadout.Attacks.Add(new()
        {
            Attack = new()
            {
                Id = "magic",
                Action = CombatActionKind.Magic,
                DamageKind = CombatDamageKind.Bolt,
                ManaCost = new() { 1 },
                BaseDamage = new() { 100 },
                StatPercent = new() { 0 },
            }
        });
        var snapshot = new CombatSnapshot { ManaRecoveryDelay = 30 };
        var scenario = new CombatScenario { DurationSeconds = 60, InitialManaFraction = 0, WeaponSequence = new(), CastingSeconds = 0.1 };
        var baseline = CombatSimulator.Evaluate(loadout, snapshot, scenario);
        loadout.Attacks.Add(new()
        {
            Attack = new() { Id = "automatic", Action = CombatActionKind.Automatic, IntervalByLevel = new() { 0.1 }, Multiplier = 0 }
        });
        var fragmented = CombatSimulator.Evaluate(loadout, snapshot, scenario);
        Assert.Equal(baseline.TotalDamage, fragmented.TotalDamage);
        Assert.Equal(baseline.RemainingMana, fragmented.RemainingMana, 10);
        Assert.Equal(Assert.Single(baseline.Contributions).Uses, fragmented.Contributions.Single(part => part.Id == "0:magic").Uses);
    }

    [Fact]
    public void InvalidScenarioCollectionsAndWeaponActionsFailWithASettingError()
    {
        foreach (var scenario in new CombatScenario[]
        {
            null!, new() { WeaponSequence = null! }, new() { TargetStats = null! },
            new() { MeasuredActionSeconds = null! }, new() { WeaponSequence = new() { CombatActionKind.Magic } },
        }) Assert.Throws<ArgumentException>(() => CombatSimulator.Validate(scenario));
    }

    [Theory]
    [InlineData(0.1, 1, 10)]
    [InlineData(0.1, 30, 300)]
    [InlineData(0.2, 2, 10)]
    public void FractionalAttackPeriodsExcludeTheFightEnd(double interval, double duration, int expectedUses)
    {
        var loadout = new CombatLoadout();
        loadout.Stats.SetSource(new() { Id = "weapon", Stats = { ["PHYSICALDAMAGE"] = 100 } });
        loadout.Attacks.Add(new() { Attack = new() { DamageKind = CombatDamageKind.Weapon } });
        var result = CombatSimulator.Evaluate(loadout, new(), new() { DurationSeconds = duration, BasicSeconds = interval });
        Assert.Equal(expectedUses, Assert.Single(result.Contributions).Uses);
    }

    [Fact]
    public void ATriggerReadyOnEveryWeaponAttackDoesNotMissHitsThroughRounding()
    {
        var loadout = new CombatLoadout();
        loadout.Stats.SetSource(new() { Id = "weapon", Stats = { ["PHYSICALDAMAGE"] = 100, ["FIREDAMAGE"] = 100 } });
        loadout.Attacks.Add(new() { Attack = new() { Id = "weapon", DamageKind = CombatDamageKind.Weapon } });
        loadout.Attacks.Add(new()
        {
            Attack = new()
            {
                Id = "trigger",
                Action = CombatActionKind.Automatic,
                DamageKind = CombatDamageKind.FlameSword,
                Trigger = CombatTrigger.AttackHit,
                TriggerCooldownSeconds = 0.1,
                Charges = 30,
                Recharges = false,
            }
        });
        var result = CombatSimulator.Evaluate(loadout, new(), new() { DurationSeconds = 3, BasicSeconds = 0.1 });
        Assert.All(result.Contributions, part => Assert.Equal(30, part.Uses));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(7, 3)]
    [InlineData(3, 7)]
    [InlineData(13, 17)]
    [InlineData(19, 23)]
    public void MagicOnlyContinuousRegenerationDoesNotStallOnRounding(int cost, int regeneration)
    {
        var loadout = new CombatLoadout();
        loadout.Stats.SetSource(new() { Id = "mana", Stats = { ["@MAXMP"] = 100, ["MPREGEN"] = regeneration } });
        loadout.Attacks.Add(new()
        {
            DefinitionId = 1,
            Attack = new()
            {
                Action = CombatActionKind.Magic,
                DamageKind = CombatDamageKind.Bolt,
                ManaCost = new() { cost },
                BaseDamage = new() { 100 },
                StatPercent = new() { 0 },
                CooldownSeconds = 0,
            }
        });
        var result = CombatSimulator.Evaluate(loadout, new(), new()
        {
            DurationSeconds = 60,
            InitialManaFraction = 0,
            WeaponSequence = new(),
            CastingSeconds = 0.1,
        });
        Assert.True(result.TotalDamage > 0);
        Assert.InRange(result.RemainingMana, 0, cost);
    }
}
