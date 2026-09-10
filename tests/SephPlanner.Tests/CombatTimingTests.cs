using SephPlanner.Core.Combat;

namespace SephPlanner.Tests;

public sealed class CombatTimingTests
{
    [Fact]
    public void MeasuredIntervalsRemoveCurrentSpeedAndStillValueFutureSpeedChanges()
    {
        var snapshot = Snapshot();
        var original = new CombatScenario { DurationSeconds = 4, BasicSeconds = 2 };
        var calibrated = CombatTiming.Calibrate(snapshot, original, "일반=0.5,마법=0.75");
        Assert.Empty(original.MeasuredActionSeconds);
        Assert.Equal(1, calibrated.MeasuredActionSeconds["Basic"]);
        Assert.Equal(0.75, calibrated.MeasuredActionSeconds["Magic"]);
        Assert.Equal(200, Evaluate(snapshot, calibrated, 100).Dps);
        Assert.Equal(400, Evaluate(snapshot, calibrated, 300).Dps);
        Assert.Equal(100, Evaluate(snapshot, original, 100).Dps);
    }

    [Fact]
    public void ChangingWeaponsStopsApplyingTheOldCalibration()
    {
        var snapshot = Snapshot();
        var calibrated = CombatTiming.Calibrate(snapshot, new() { DurationSeconds = 4, BasicSeconds = 2 }, "일반=0.5");
        snapshot.WeaponAttacks[0].Id = "다른 무기";
        var result = Evaluate(snapshot, calibrated, 100);
        Assert.Equal(100, result.Dps);
        Assert.Contains(result.Unsupported, message => message.Contains("무기가 달라", StringComparison.Ordinal));
        Assert.Equal(1, calibrated.MeasuredActionSeconds["Basic"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("일반=NaN")]
    [InlineData("일반=0")]
    [InlineData("일반=0.5,일반=0.4")]
    [InlineData("일반=0.5,없는동작=1")]
    [InlineData("대시=0.5")]
    public void InvalidOrUnavailableMeasurementsDoNotPartiallyChangeSettings(string input)
    {
        var original = new CombatScenario();
        Assert.Throws<ArgumentException>(() => CombatTiming.Calibrate(Snapshot(), original, input));
        Assert.Empty(original.MeasuredActionSeconds);
    }

    private static CombatSnapshot Snapshot() => new()
    {
        ObservedStats = { ["ATTACKSPEED"] = 100 },
        WeaponAttacks = { new() { Id = "weapon:1:Basic:0", DamageKind = CombatDamageKind.Weapon } }
    };

    private static CombatResult Evaluate(CombatSnapshot snapshot, CombatScenario scenario, int speed)
    {
        var loadout = new CombatLoadout();
        loadout.Stats.SetSource(new() { Id = "현재 배치", Stats = { ["PHYSICALDAMAGE"] = 100, ["ATTACKSPEED"] = speed } });
        loadout.Attacks.Add(new() { Attack = snapshot.WeaponAttacks[0] });
        return CombatSimulator.Evaluate(loadout, snapshot, scenario);
    }
}
