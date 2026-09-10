using SephPlanner.Core.Combat;

namespace SephPlanner.Tests;

public sealed class CombatElementTests
{
    [Theory]
    [InlineData("CHAOS", 90)]
    [InlineData("FIREANDICE", 100)]
    [InlineData("FIREANDLIGHTNING", 100)]
    [InlineData("ICEANDLIGHTNING", 120)]
    [InlineData("NORMAL", 200)]
    public void MixedElementsUseTheHighestMatchingResistance(string element, double expected)
    {
        var scenario = new CombatScenario
        {
            TargetStats = new()
            {
                ["FIREDEFENSE"] = 50,
                ["ICEDEFENSE"] = 20,
                ["LIGHTNINGDEFENSE"] = 40,
                ["PHYSICALDEFENSE"] = 55,
            }
        };
        Assert.Equal(expected, CombatDamage.ExpectedHit(new() { Element = element }, 200, new(), scenario));
    }

    [Fact]
    public void ResistanceUsesFloatSubtractionBeforeIntegerDamageConversion()
    {
        Assert.Equal(90, CombatDamage.ExpectedHit(new(), 200, new(), new() { TargetStats = new() { ["PHYSICALDEFENSE"] = 55 } }));
    }

    [Theory]
    [InlineData("PHYSICALDAMAGE", true, 90)]
    [InlineData("FIREDAMAGE", true, 100)]
    [InlineData("FIREDAMAGE", false, 90)]
    [InlineData("HIGHEST", true, 100)]
    [InlineData("HIGHEST/", true, 100)]
    [InlineData("LOWEST", true, 160)]
    [InlineData("AVERAGEALL", true, 90)]
    [InlineData("AVERAGE/FIREDAMAGE,ICEDAMAGE", true, 90)]
    [InlineData("UNKNOWN", true, 200)]
    [InlineData("", true, 90)]
    public void WeaponDamageUsesTheResolvedElementsResistanceOnlyWhenEnabled(string formula, bool dynamic, double expected)
    {
        var stats = new CombatStatState();
        stats.SetSource(new()
        {
            Id = "elements",
            Stats = { ["FIREDAMAGE"] = 200, ["ICEDAMAGE"] = 50, ["LIGHTNINGDAMAGE"] = 100, ["PHYSICALDAMAGE"] = 100 }
        });
        var scenario = new CombatScenario
        {
            TargetStats = new()
            {
                ["FIREDEFENSE"] = 50,
                ["ICEDEFENSE"] = 20,
                ["LIGHTNINGDEFENSE"] = 40,
                ["PHYSICALDEFENSE"] = 55,
            }
        };
        var attack = new CombatAttack { DamageKind = CombatDamageKind.Weapon, Stat = formula, Element = "PHYSICAL", ElementFromRelatedStat = dynamic };
        // 200 피해에 속성 저항만 적용해 관련 능력치 배수와 분리해 검사한다.
        Assert.Equal(expected, CombatDamage.ExpectedHit(attack, 200, stats, scenario));
    }

    [Fact]
    public void HighestElementTieUsesFireBeforeIceAndReevaluatesChangedStats()
    {
        var stats = new CombatStatState();
        stats.SetSource(new() { Id = "base", Stats = { ["FIREDAMAGE"] = 100, ["ICEDAMAGE"] = 100 } });
        var attack = new CombatAttack { DamageKind = CombatDamageKind.Weapon, Stat = "HIGHEST", ElementFromRelatedStat = true };
        var scenario = new CombatScenario { TargetStats = new() { ["FIREDEFENSE"] = 50, ["ICEDEFENSE"] = 20 } };
        Assert.Equal(100, CombatDamage.ExpectedHit(attack, 200, stats, scenario));
        stats.SetSource(new() { Id = "base", Stats = { ["FIREDAMAGE"] = 100, ["ICEDAMAGE"] = 101 } });
        Assert.Equal(160, CombatDamage.ExpectedHit(attack, 200, stats, scenario));
    }
}
