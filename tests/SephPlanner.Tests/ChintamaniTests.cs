using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public class ChintamaniTests
{
    private static readonly GridPos BonusCell = new(1, 0);

    private static Catalog Catalog() => new(Array.Empty<TabletDefinition>(),
        new[] { new CharmDefinition { Id = "A", EntityId = 1, MaxLevel = 20 } });

    private static GameSnapshot Snapshot(bool multiplayer = false)
    {
        var inventory = new InventoryState { Width = 2, Height = 1, Storage = 2 };
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 1,
            InstanceId = 10,
            Position = new GridPos(0, 0),
            IsActive = true,
        });
        inventory.FixedEffects.Add(new FixedEffectCell { Position = BonusCell, Level = 3 });
        inventory.LevelMatrix["1,0"] = 3;
        return new GameSnapshot { Inventory = inventory, IsMultiplayer = multiplayer };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConsumedChintamanisEmptyCellCanBeUsedAndThePlanSettles(bool multiplayer)
    {
        var snapshot = Snapshot(multiplayer);
        var preferences = new PlanPreferences { Recommendations = false };
        var first = PlanBuilder.Build(snapshot, Catalog(), preferences)!;

        Assert.True(first.Verification.Passed);
        Assert.Equal(BonusCell, first.Best.CharmPositions[10]);
        Assert.Equal(3, first.CreateApplyCommand().ExpectedCellLevels[BonusCell]);

        var item = snapshot.Inventory!.Items[0];
        item.Position = BonusCell;
        item.EffectiveLevel = 3;
        var next = PlanBuilder.Build(snapshot, Catalog(), preferences, first)!;
        Assert.True(next.Verification.Passed);
        Assert.False(next.HasPlacementChanges);

        // 아이템을 옮겨도 보너스는 원래 좌표에 남는다.
        item.Position = new GridPos(0, 0);
        item.EffectiveLevel = 0;
        var moved = PlanBuilder.Build(snapshot, Catalog(), preferences)!;
        Assert.True(moved.Verification.Passed);
        Assert.Equal(0, moved.Current.CellLevels[item.Position]);
        Assert.Equal(3, moved.Current.CellLevels[BonusCell]);
    }

    [Fact]
    public void TemporaryLevelsStackWithEngravingsAndEnchantBeforeMultiplication()
    {
        var snapshot = Snapshot();
        var inventory = snapshot.Inventory!;
        inventory.FixedEffects.Add(new FixedEffectCell { Position = BonusCell, Level = 3 });
        inventory.FixedEffects.Add(new FixedEffectCell { Position = BonusCell, Level = 2, Multiply = 2 });
        inventory.Items[0].Position = BonusCell;
        inventory.Items[0].Enchant = 1;
        inventory.Items[0].EffectiveLevel = 18;
        inventory.LevelMatrix["1,0"] = 18;

        var plan = PlanBuilder.Build(snapshot, Catalog(), new PlanPreferences { Recommendations = false })!;

        Assert.True(plan.Verification.Passed);
        Assert.Equal(18, plan.Current.CellLevels[BonusCell]);
    }

    [Fact]
    public void ClearingTheGamesTemporaryBonusInvalidatesThePlanAndRemovesTheEffect()
    {
        var snapshot = Snapshot();
        var before = PlanFingerprint.Placement(snapshot, "test");
        snapshot.Inventory!.FixedEffects.Clear();
        snapshot.Inventory.LevelMatrix.Clear();

        Assert.NotEqual(before, PlanFingerprint.Placement(snapshot, "test"));
        var plan = PlanBuilder.Build(snapshot, Catalog(), new PlanPreferences { Recommendations = false })!;
        Assert.True(plan.Verification.Passed);
        Assert.Equal(0, plan.Current.CellLevels[BonusCell]);
        Assert.False(plan.HasPlacementChanges);
    }
}
