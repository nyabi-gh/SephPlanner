using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class MysticEngravingsTests
{
    private static readonly MysticRule Rule = new()
    {
        FirstThreshold = 2,
        FirstCount = 1,
        SecondThreshold = 5,
        SecondCount = 3,
        Query = "O MUL/2",
    };
    private static readonly GridSpec Grid = new(6, 7, 12);
    private static readonly GridPos[] Positions = { new(3, 1), new(1, 0), new(2, 0), new(3, 0) };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 4)]
    [InlineData(20, 4)]
    public void BothThresholdsUseThePublishedPositions(int count, int expected)
    {
        var effects = MysticEngravings.Resolve(Rule, count, Positions, Grid);
        Assert.Equal(expected, effects.Count);
        Assert.Equal(Positions.Take(expected), effects.Select(effect => effect.Position));
        Assert.All(effects, effect => Assert.Equal(2, effect.Multiply));
    }

    [Fact]
    public void MissingPositionsAreNotReplacedWithTheOrigin()
    {
        var effects = MysticEngravings.Resolve(Rule, 5, Positions.Take(1).ToArray(), Grid);
        Assert.Equal(Positions[0], Assert.Single(effects).Position);
        Assert.Empty(MysticEngravings.Resolve(Rule, 5, Array.Empty<GridPos>(), Grid));
    }

    [Fact]
    public void TheRuleComesFromDataRatherThanHardcodedThresholdsOrMultipliers()
    {
        var rule = new MysticRule
        {
            FirstThreshold = 3,
            FirstCount = 2,
            SecondThreshold = 7,
            SecondCount = 1,
            Query = "O MUL/3",
        };
        Assert.Empty(MysticEngravings.Resolve(rule, 2, Positions, Grid));
        var effects = MysticEngravings.Resolve(rule, 3, Positions, Grid);
        Assert.Equal(2, effects.Count);
        Assert.All(effects, effect => Assert.Equal(3, effect.Multiply));
    }

    [Fact]
    public void AZeroLevelStillRetainsItsMysticMultiplier()
    {
        var effects = MysticEngravings.Resolve(Rule, 2, Positions, Grid);
        var result = TabletSimulator.Run(Array.Empty<TabletPlacement>(), new GridOccupancy(), Grid, effects);
        Assert.Equal(0, result.EffectiveLevel(Positions[0], 0));
        Assert.Equal(2, result.MultiplierAt(Positions[0]));
        Assert.Equal(6, result.EffectiveLevel(Positions[0], 3));
    }

    [Fact]
    public void AConditionalMysticDoesNotGuessItsCreationTimeOccupancy()
    {
        var rule = new MysticRule
        {
            FirstThreshold = 2,
            FirstCount = 1,
            SecondThreshold = 5,
            Query = "O MUL/2",
            ConditionQuery = "O ANY",
        };
        Assert.Throws<InvalidOperationException>(() => MysticEngravings.Resolve(rule, 2, Positions, Grid));
    }

    [Fact]
    public void MysticAndTabletMultipliersAddAndThenMultiplyEnchantment()
    {
        var effects = MysticEngravings.Resolve(Rule, 2, Positions, Grid);
        var tablet = new TabletPlacement
        {
            Definition = new TabletDefinition { Query = "RIGHT 2\nRIGHT MUL/3" },
            Position = new(2, 1),
        };
        var result = TabletSimulator.Run(new[] { tablet }, new GridOccupancy(), Grid, effects);
        Assert.Equal(5, result.MultiplierAt(Positions[0]));
        Assert.Equal(15, result.EffectiveLevel(Positions[0], enchant: 1));
    }

    [Fact]
    public void ReconstructedMysticFixesBothChecksForTheSameCell()
    {
        // 스크린샷과 같은 (3,1) 칸 3→6. 한 칸이 행렬·아이템 검증에서 각각 불일치한다.
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 12 };
        inventory.Tablets.Add(new PlacedTablet { InstanceId = 1, DefinitionId = 100, Position = new(2, 1), IsApplied = true });
        inventory.Items.Add(new PlacedItem
        {
            InstanceId = 2,
            DefinitionId = 200,
            Position = Positions[0],
            EffectiveLevel = 6,
            IsActive = true,
        });
        inventory.LevelMatrix["3,1"] = 6;
        var snapshot = new GameSnapshot { IsMultiplayer = true, Inventory = inventory };
        var catalog = new Catalog(
            new[] { new TabletDefinition { EntityId = 100, Query = "RIGHT 3" } },
            new[] { new CharmDefinition { EntityId = 200, MaxLevel = 10 } });
        var before = PlanBuilder.Build(snapshot, catalog)!;
        Assert.Equal(1, before.Verification.LevelMismatches);
        Assert.Equal(1, before.Verification.EffectiveLevelMismatches);
        inventory.FixedEffects = MysticEngravings.Resolve(Rule, 2, Positions, Grid);
        var after = PlanBuilder.Build(snapshot, catalog)!;
        Assert.True(after.Verification.Passed, after.Verification.Reason);
        Assert.Equal(6, after.Current.CellLevels[Positions[0]]);
    }
}
