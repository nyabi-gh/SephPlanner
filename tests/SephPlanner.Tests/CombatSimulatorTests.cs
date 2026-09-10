using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public sealed class CombatSimulatorTests
{
    [Theory]
    [InlineData(CombatActionKind.Basic, 100)]
    [InlineData(CombatActionKind.Dash, 200)]
    public void DashBonusFollowsTheSelectedAttack(CombatActionKind action, double expected)
    {
        var loadout = Weapon(action);
        loadout.Stats.SetSource(new() { Id = "dash", Stats = { ["DASHATTACKDAMAGEBONUS"] = 100 } });
        var result = Run(loadout, new() { WeaponSequence = { }, DurationSeconds = 30, });
        Assert.Equal(action == CombatActionKind.Basic ? expected : 0, result.Dps);
        result = Run(loadout, new() { WeaponSequence = new() { action } });
        Assert.Equal(expected, result.Dps);
        Assert.Equal(30, Assert.Single(result.Contributions).Uses);
    }

    [Fact]
    public void DamageAndAttackBonusesAreMultiplicative()
    {
        var loadout = Weapon(CombatActionKind.Dash);
        loadout.Stats.SetSource(new() { Id = "bonus", Stats = { ["PHYSICALDAMAGE"] = 20, ["DASHATTACKDAMAGEBONUS"] = 20 } });
        Assert.Equal(144, Run(loadout, new() { WeaponSequence = new() { CombatActionKind.Dash } }).Dps);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 100.5)]
    [InlineData(10000, 150)]
    [InlineData(15000, 175)]
    [InlineData(20000, 200)]
    public void CriticalUnitsAndExecutionFollowGameDamage(int chance, double expected)
    {
        var loadout = Weapon();
        loadout.Stats.SetSource(new() { Id = "critical", Stats = { ["CRITICAL"] = chance, ["EXECUTION"] = 1 } });
        Assert.Equal(expected, Run(loadout).Dps, 6);
    }

    [Fact]
    public void SharedManaAndPriorityPreventDoubleSpending()
    {
        var loadout = Mana(10);
        loadout.Attacks.Add(Magic(1, 100, 10, 2, 100));
        loadout.Attacks.Add(Magic(2, 200, 10, 2, 100));
        var result = Run(loadout, new() { WeaponSequence = new(), MagicPriority = new() { 2, 1 } });
        Assert.Equal(200, result.TotalDamage);
        Assert.Equal("2:magic", Assert.Single(result.Contributions).Id);
        Assert.Equal(0, result.RemainingMana);
    }

    [Fact]
    public void MagicWaitsForManaAfterTheObservedRecoveryDelay()
    {
        var loadout = Mana(10);
        loadout.Stats.SetSource(new() { Id = "regen", Stats = { ["MPREGEN"] = 10 } });
        loadout.Attacks.Add(Magic(1, 100, 10, 2, 100));
        var result = CombatSimulator.Evaluate(loadout, new() { ManaRecoveryDelay = 100 }, new() { WeaponSequence = new(), DurationSeconds = 12 });
        Assert.Equal(2, Assert.Single(result.Contributions).Uses);
        Assert.Equal(0, result.RemainingMana, 6);
    }

    [Fact]
    public void MagicChargesRecoverSequentiallyAndStopAtHorizon()
    {
        var loadout = Mana(0);
        loadout.Attacks.Add(Magic(1, 100, 0, 2, 5));
        var result = Run(loadout, new() { WeaponSequence = new(), DurationSeconds = 10 });
        Assert.Equal(3, Assert.Single(result.Contributions).Uses);
        Assert.Equal(300, result.TotalDamage);
    }

    [Fact]
    public void EmptyChargesBeginWithRechargeInsteadOfFreeUses()
    {
        var loadout = Mana(0);
        loadout.Attacks.Add(Magic(1, 100, 0, 2, 5));
        Assert.Equal(1, Assert.Single(Run(loadout, new() { WeaponSequence = new(), InitialChargeFraction = 0, DurationSeconds = 10 }).Contributions).Uses);
    }

    [Fact]
    public void ZeroCooldownStillRespectsCastingTime()
    {
        var loadout = Mana(0);
        loadout.Attacks.Add(Magic(1, 100, 0, 1, 0));
        Assert.Equal(30, Assert.Single(Run(loadout, new() { WeaponSequence = new() }).Contributions).Uses);
    }

    [Fact]
    public void MagicCastingOccupiesTheWeaponActionTime()
    {
        var loadout = Weapon();
        loadout.Attacks.Add(Magic(1, 200, 0, 1, 100));
        var result = Run(loadout, new() { DurationSeconds = 3 });
        Assert.Equal(400, result.TotalDamage);
        Assert.Equal(2, result.Contributions.Single(value => value.Name == "일반").Uses);
    }

    [Fact]
    public void AreaAttacksAndSingleTargetAttacksScaleDifferently()
    {
        var loadout = Weapon();
        Assert.Equal(100, Run(loadout, new() { TargetCount = 3 }).Dps);
        loadout.Attacks[0].Attack.MaxTargets = 0;
        Assert.Equal(200, Run(loadout, new() { TargetCount = 3 }).Dps);
        Assert.Equal(300, Run(loadout, new() { TargetCount = 3, AdditionalTargetFraction = 1 }).Dps);
        Assert.Equal(100, Run(loadout, new() { TargetCount = 3, AdditionalTargetFraction = 0 }).Dps);
        loadout.Attacks[0].Attack.MaxTargets = 2;
        Assert.Equal(150, Run(loadout, new() { TargetCount = 3 }).Dps);
    }

    [Fact]
    public void OpeningAndEndingWindowsSeparateInitialStockFromLaterDamage()
    {
        var loadout = Weapon();
        loadout.Attacks.Add(Magic(1, 500, 0, 1, 100));
        var result = Run(loadout, new() { DurationSeconds = 10, ComparisonWindowSeconds = 2 });
        Assert.Equal(140, result.Dps);
        Assert.Equal(300, result.OpeningDps);
        Assert.Equal(100, result.EndingDps);
        Assert.Equal(2, result.ComparisonWindowSeconds);
        var empty = Run(loadout, new() { DurationSeconds = 10, InitialManaFraction = 0, InitialChargeFraction = 0 });
        Assert.Equal(100, empty.Dps);
    }

    [Fact]
    public void ShortCombatClampsWindowAndCountsBoundaryHitsOncePerWindow()
    {
        var result = Run(Weapon(), new() { DurationSeconds = 2.5, ComparisonWindowSeconds = 10 });
        Assert.Equal(2.5, result.ComparisonWindowSeconds);
        Assert.Equal(result.Dps, result.OpeningDps);
        Assert.Equal(result.Dps, result.EndingDps);
        result = Run(Weapon(), new() { DurationSeconds = 4, ComparisonWindowSeconds = 2 });
        Assert.Equal(200, result.OpeningDamage);
        Assert.Equal(200, result.EndingDamage);
    }

    [Fact]
    public void FlameSwordStartsWithStockAndCannotRegenerateWithoutARecoveryPath()
    {
        var loadout = Weapon();
        loadout.Stats.SetSource(new() { Id = "fire", Stats = { ["FIREDAMAGE"] = 100, ["ICEDAMAGE"] = 20 } });
        loadout.Attacks.Add(new()
        {
            Attack = new()
            {
                Id = "sword",
                Name = "화염검",
                Action = CombatActionKind.Automatic,
                DamageKind = CombatDamageKind.FlameSword,
                Trigger = CombatTrigger.AttackHit,
                StatPercent = new() { 150 },
                Charges = 4,
                Recharges = false,
                TriggerCooldownSeconds = 0.15,
            }
        });
        var before = Run(loadout);
        Assert.Equal(4, before.Contributions.Single(value => value.Name == "화염검").Uses);
        Assert.Equal(600, before.Contributions.Single(value => value.Name == "화염검").Damage);
        loadout.Stats.SetSource(new() { Id = "eclipse", Stats = { ["FLAMESWORDFROST"] = 1 } });
        Assert.Equal(120, Run(loadout).Contributions.Single(value => value.Name == "화염검").Damage);
    }

    [Fact]
    public void UnknownEffectsAreReportedWithoutBlockingTheEstimate()
    {
        var loadout = Weapon();
        loadout.Unsupported.Add("확인하지 못한 소환 효과");
        loadout.Stats.SetSource(new() { Id = "unknown", Stats = { ["NEW_EFFECT"] = 10 } });
        var result = Run(loadout);
        Assert.Equal(100, result.Dps);
        Assert.Contains("확인하지 못한 소환 효과", result.Unsupported);
        Assert.Contains(result.Unsupported, message => message.Contains("NEW_EFFECT", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidScenarioAndCancellationAreErrors()
    {
        Assert.Throws<ArgumentException>(() => Run(Weapon(), new() { DurationSeconds = double.NaN }));
        Assert.Throws<ArgumentException>(() => Run(Weapon(), new() { AdditionalTargetFraction = double.NaN }));
        Assert.Throws<ArgumentException>(() => Run(Weapon(), new() { AdditionalTargetFraction = 1.1 }));
        Assert.Throws<ArgumentException>(() => Run(Weapon(), new() { ComparisonWindowSeconds = 0 }));
        Assert.Throws<OperationCanceledException>(() => CombatSimulator.Evaluate(Weapon(), new(), new(), cancellation: new(true)));
    }

    [Fact]
    public void SourceReplacementDoesNotLeaveTheCurrentBonusInTheBaseline()
    {
        var definition = new CharmDefinition
        {
            EntityId = 1,
            Combat = new()
            {
                Collected = true,
                Stats = { new() { Key = "PHYSICALDAMAGE", Values = new() { 20, 40 } } }
            }
        };
        var problem = new PlacementProblem { Grid = new(2, 1, 2), Charms = { new() { InstanceId = 1, Definition = definition } } };
        var positions = new Dictionary<int, GridPos> { [1] = new(0, 0) };
        var current = PlacementSolver.Score(problem, Array.Empty<TabletPlacement>(), positions);
        var snapshot = new CombatSnapshot { ObservedStats = { ["PHYSICALDAMAGE"] = 120 }, WeaponAttacks = { Weapon().Attacks[0].Attack } };
        problem.Combat = CombatPlanning.Capture(problem, current, snapshot, new());
        Assert.Equal(120, PlacementSolver.Score(problem, Array.Empty<TabletPlacement>(), positions).Score);
        problem.FixedEffects.Add(new() { Position = new(0, 0), Level = 1 });
        Assert.Equal(140, PlacementSolver.Score(problem, Array.Empty<TabletPlacement>(), positions).Score);
        problem.Charms.Clear();
        Assert.Equal(100, PlacementSolver.Score(problem, Array.Empty<TabletPlacement>(), new Dictionary<int, GridPos>()).Score);
    }

    [Fact]
    public void PositionChangesReplaceBothEclipseFlags()
    {
        var charm = new CharmSlot { InstanceId = 1, Definition = new() { Combat = new() { Collected = true, FireIcePosition = true } } };
        var problem = new PlacementProblem { Grid = new(6, 1, 6), Charms = { charm } };
        var context = new PlacementCombatContext();
        var left = CombatLoadouts.Build(problem, context, new[] { new LocatedCombatCharm { Charm = charm, Active = true, Position = new(2, 0) } });
        var right = CombatLoadouts.Build(problem, context, new[] { new LocatedCombatCharm { Charm = charm, Active = true, Position = new(3, 0) } });
        Assert.Equal(1, left.Stats.Read("FROSTRELICFLAME"));
        Assert.Equal(0, left.Stats.Read("FLAMESWORDFROST"));
        Assert.Equal(0, right.Stats.Read("FROSTRELICFLAME"));
        Assert.Equal(1, right.Stats.Read("FLAMESWORDFROST"));
    }

    [Fact]
    public void FingerprintsIncludeScenarioAndCombatInputsButIgnoreDictionaryOrder()
    {
        var snapshot = new GameSnapshot { Run = new() { Combat = new() { ObservedStats = { ["PHYSICALDAMAGE"] = 100, ["CRITICAL"] = 100 } } } };
        var preferences = new PlanPreferences();
        var first = PlanFingerprint.Placement(snapshot, preferences, "catalog");
        snapshot.Run.Combat.ObservedStats = new() { ["CRITICAL"] = 100, ["PHYSICALDAMAGE"] = 100 };
        Assert.Equal(first, PlanFingerprint.Placement(snapshot, preferences, "catalog"));
        preferences.Combat.DurationSeconds = 10;
        Assert.NotEqual(first, PlanFingerprint.Placement(snapshot, preferences, "catalog"));
        preferences.Combat.DurationSeconds = 30;
        snapshot.Run.Combat.ObservedStats["CRITICAL"]++;
        Assert.NotEqual(first, PlanFingerprint.Placement(snapshot, preferences, "catalog"));
    }

    private static CombatLoadout Weapon(CombatActionKind action = CombatActionKind.Basic)
    {
        var loadout = Mana(0);
        loadout.Stats.SetSource(new() { Id = "weapon", Stats = { ["PHYSICALDAMAGE"] = 100 } });
        loadout.Attacks.Add(new() { Attack = new() { Id = "weapon", Name = "일반", Action = action, DamageKind = CombatDamageKind.Weapon } });
        return loadout;
    }

    private static CombatLoadout Mana(int mana)
    {
        var loadout = new CombatLoadout();
        loadout.Stats.SetSource(new() { Id = "mana", Stats = { ["@MAXMP"] = mana } });
        return loadout;
    }

    private static CombatAttackInstance Magic(int id, int damage, int cost, int charges, int cooldown) => new()
    {
        DefinitionId = id,
        InstanceId = id,
        Attack = new()
        {
            Id = "magic",
            Name = "마법",
            Action = CombatActionKind.Magic,
            DamageKind = CombatDamageKind.Bolt,
            BaseDamage = new() { damage },
            StatPercent = new() { 0 },
            ManaCost = new() { cost },
            Charges = charges,
            CooldownSeconds = cooldown
        },
    };

    private static CombatResult Run(CombatLoadout loadout, CombatScenario? scenario = null) =>
        CombatSimulator.Evaluate(loadout, new(), scenario ?? new());
}
