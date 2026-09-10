using Newtonsoft.Json;
using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public sealed class ReportedCombatEffectsTests
{
    [Theory]
    [InlineData(CombatActionKind.Basic)]
    [InlineData(CombatActionKind.Dash)]
    [InlineData(CombatActionKind.Special)]
    public void DirectCriticalAppliesBeforeResistanceAndExecutionWithoutChangingObservedStats(CombatActionKind action)
    {
        var problem = Problem(new() { Collected = true, DirectAttackCriticalByLevel = new() { 10, 20 } });
        var snapshot = Snapshot(action);
        snapshot.ObservedStats["CRITICAL"] = 9500;
        snapshot.ObservedStats["EXECUTION"] = 1;
        var current = PlacementSolver.Score(problem, [], problem.CurrentCharms);
        var scenario = new CombatScenario { WeaponSequence = new() { action } };
        var context = CombatPlanning.Capture(problem, current, snapshot, scenario);
        var item = new LocatedCombatCharm { Charm = problem.Charms[0], Position = new(0, 0), Active = true };
        var loadout = CombatLoadouts.Build(problem, context, new[] { item });
        Assert.Equal(9500, loadout.Stats.Read("CRITICAL"));
        Assert.Equal(0, loadout.Stats.Read("WEAPONCRITICAL"));
        Assert.Equal(152.5, CombatSimulator.Evaluate(loadout, snapshot, scenario).Dps);
        scenario.TargetStats["CRITICALRESIST"] = 50;
        Assert.Equal(126.25, CombatSimulator.Evaluate(loadout, snapshot, scenario).Dps);
        scenario.TargetStats.Clear();
        item.Level = 1;
        Assert.Equal(157.5, CombatSimulator.Evaluate(CombatLoadouts.Build(problem, context, new[] { item }), snapshot, scenario).Dps);
        item.Active = false;
        Assert.Equal(147.5, CombatSimulator.Evaluate(CombatLoadouts.Build(problem, context, new[] { item }), snapshot, scenario).Dps);
        Assert.Equal(147.5, CombatSimulator.Evaluate(CombatLoadouts.Build(problem, context, []), snapshot, scenario).Dps);
    }

    [Theory]
    [InlineData(CombatDamageKind.Bolt)]
    [InlineData(CombatDamageKind.IceRelic)]
    [InlineData(CombatDamageKind.FlameSword)]
    public void ArtifactCriticalBonusDoesNotLeakToMagicOrAutomaticDamage(CombatDamageKind kind)
    {
        var stats = new CombatStatState();
        var attack = new CombatAttack { DamageKind = kind, Action = CombatActionKind.Automatic };
        Assert.Equal(100, CombatDamage.ExpectedHit(attack, 100, stats, new(), 100));
    }

    [Theory]
    [InlineData(1, 150)]
    [InlineData(2, 175)]
    public void ScytheExecutionCanBeRemovedWithoutRemovingOtherSources(int observed, double removedDps)
    {
        var problem = Problem(new() { Collected = true, Stats = { new() { Key = "EXECUTION", Values = new() { 1 } } } });
        var snapshot = Snapshot(CombatActionKind.Basic);
        snapshot.ObservedStats["EXECUTION"] = observed;
        snapshot.ObservedStats["CRITICAL"] = 15000;
        var current = PlacementSolver.Score(problem, [], problem.CurrentCharms);
        var context = CombatPlanning.Capture(problem, current, snapshot, new());
        var item = new LocatedCombatCharm { Charm = problem.Charms[0], Position = new(0, 0), Active = true };
        Assert.Equal(observed, CombatLoadouts.Build(problem, context, new[] { item }).Stats.Read("EXECUTION"));
        Assert.Equal(175, CombatSimulator.Evaluate(CombatLoadouts.Build(problem, context, new[] { item }), snapshot, new()).Dps);
        Assert.Equal(removedDps, CombatSimulator.Evaluate(CombatLoadouts.Build(problem, context, []), snapshot, new()).Dps);
    }

    [Fact]
    public void KnownHealthChangesShowThePenaltyButMovingUnchangedHealthIsAllowed()
    {
        var problem = Problem(new() { Collected = true, Stats = { new() { Key = "@FINALHP", Values = new() { -3, -6 } } } });
        problem.Combat = new();
        var before = new Arrangement { CharmPositions = { [1] = new(0, 0) } };
        var after = new Arrangement { CharmPositions = { [1] = new(1, 0) } };
        Assert.Empty(CombatChangeAssessment.Compare(problem, before, after));
        after.Levels[new(1, 0)] = 1;
        Assert.Contains("최대 체력 증감률 -3% → -6%", Assert.Single(CombatChangeAssessment.Compare(problem, before, after)));
        after.InactiveCharms.Add(1);
        Assert.Contains("-3% → 0%", Assert.Single(CombatChangeAssessment.Compare(problem, before, after)));
        after.CharmPositions.Clear();
        Assert.Contains("-3% → 0%", Assert.Single(CombatChangeAssessment.Compare(problem, before, after)));
        problem.Charms[0].Definition.Combat.Unsupported.Add("시간에 따른 효과 미지원");
        Assert.Equal(2, CombatChangeAssessment.Compare(problem, before, after).Count);
    }

    [Fact]
    public void TimedEffectsRemainProtectedAndMetadataRoundTrips()
    {
        var effect = new CharmCombatEffect { Collected = true, DirectAttackCriticalByLevel = new() { 2, 4.5, 8 } };
        var restored = System.Text.Json.JsonSerializer.Deserialize<CharmCombatEffect>(JsonConvert.SerializeObject(effect))!;
        Assert.Equal(effect.DirectAttackCriticalByLevel, restored.DirectAttackCriticalByLevel);
        var problem = Problem(new() { Collected = true, Unsupported = { "남은 버프 시간 미수집" } });
        problem.Combat = new();
        var current = new Arrangement { CharmPositions = { [1] = new(0, 0) } };
        var changed = new Arrangement { CharmPositions = { [1] = new(0, 0) }, Levels = { [new(0, 0)] = 1 } };
        Assert.Contains("유효 레벨 0 → 1", Assert.Single(CombatChangeAssessment.Compare(problem, current, changed)));
    }

    private static PlacementProblem Problem(CharmCombatEffect effect) => new()
    {
        Grid = new(2, 1, 2),
        Charms = { new() { InstanceId = 1, Definition = new() { EntityId = 1, MaxLevel = 1, Combat = effect } } },
        CurrentCharms = { [1] = new(0, 0) }
    };

    private static CombatSnapshot Snapshot(CombatActionKind action) => new()
    {
        ObservedStats = { ["PHYSICALDAMAGE"] = 100 },
        WeaponAttacks = { new() { Action = action, DamageKind = CombatDamageKind.Weapon } }
    };
}
