using SephPlanner.Core.Combat;

namespace SephPlanner.Tests;

public sealed class CombatManaCostTests
{
    [Theory]
    [InlineData(15, 70, 0, 4)]
    [InlineData(25, 42, 0, 14)]
    [InlineData(30, 55, 0, 14)]
    [InlineData(35, 70, 0, 10)]
    [InlineData(25, 22, -20, 14)]
    [InlineData(25, 62, 20, 14)]
    [InlineData(15, 0, 50, 22)]
    [InlineData(15, 0, 0, 15)]
    public void RoundedGameCostControlsManaAndManaScaledDamage(int baseCost, int reduction, int bonus, int expectedCost)
    {
        var loadout = Loadout(100, reduction);
        loadout.Attacks.Add(Magic(1, baseCost, bonus));
        var result = Evaluate(loadout);
        Assert.Equal(100 - expectedCost, result.RemainingMana);
        Assert.Equal(1, Assert.Single(result.Contributions).Uses);
        Assert.Equal(100 + expectedCost, result.TotalDamage, 10);
    }

    [Theory]
    [InlineData(13, 0)]
    [InlineData(14, 1)]
    [InlineData(28, 2)]
    public void SharedManaPaysTheHigherPrioritySpellAtTheExactGameCost(int mana, int expectedUses)
    {
        var loadout = Loadout(mana, 42);
        loadout.Attacks.Add(Magic(1, 25));
        loadout.Attacks.Add(Magic(2, 25));
        var result = Evaluate(loadout);
        Assert.Equal(expectedUses, result.Contributions.Sum(part => part.Uses));
        Assert.Equal(mana - expectedUses * 14, result.RemainingMana);
        if (expectedUses == 1) Assert.Equal("2:마법", Assert.Single(result.Contributions).Id);
    }

    [Theory]
    [InlineData(100, 0, false)]
    [InlineData(120, 0, false)]
    [InlineData(60, -40, false)]
    [InlineData(0, 50, true)]
    public void FreeMagicCastsWithoutManaAndHasNoManaScaledDamage(int reduction, int bonus, bool noCost)
    {
        var loadout = Loadout(0, reduction);
        if (noCost) loadout.Stats.SetSource(new() { Id = "무료 마법", Stats = { ["NOMAGICCOST"] = 1 } });
        loadout.Attacks.Add(Magic(1, 25, bonus));
        var result = Evaluate(loadout);
        Assert.Equal(0, result.RemainingMana);
        Assert.Equal(1, Assert.Single(result.Contributions).Uses);
        Assert.Equal(100, result.TotalDamage);
    }

    private static CombatLoadout Loadout(int mana, int reduction)
    {
        var loadout = new CombatLoadout();
        loadout.Stats.SetSource(new()
        {
            Id = "마나",
            Stats = { ["@MAXMP"] = mana, ["MAGICCOSTREDUCE"] = reduction, ["MAGICMP"] = 10 },
        });
        return loadout;
    }

    private static CombatAttackInstance Magic(int id, int cost, int bonus = 0) => new()
    {
        DefinitionId = id,
        InstanceId = id,
        CostBonus = bonus,
        Attack = new()
        {
            Id = "마법",
            Action = CombatActionKind.Magic,
            DamageKind = CombatDamageKind.Bolt,
            ManaCost = new() { cost },
            BaseDamage = new() { 100 },
            Recharges = false,
        },
    };

    private static CombatResult Evaluate(CombatLoadout loadout) => CombatSimulator.Evaluate(loadout, new(), new()
    {
        DurationSeconds = 3,
        WeaponSequence = new(),
        MagicPriority = new() { 2, 1 },
    });
}
