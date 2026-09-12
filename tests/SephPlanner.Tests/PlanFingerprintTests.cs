using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public class PlanFingerprintTests
{
    [Fact]
    public void PaperObservationChangesInvalidatePlacementButCategoryOrderDoesNot()
    {
        var snapshot = Snapshot();
        var item = snapshot.Inventory!.Items[0];
        var unknown = PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog");
        item.ObservedCategories = new List<string> { "EMBER", "GLACIER" };
        var known = PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog");
        Assert.NotEqual(unknown, known);
        item.ObservedCategories.Reverse();
        Assert.Equal(known, PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog"));
        item.ObservedCategories.Clear();
        Assert.NotEqual(known, PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog"));
    }
    [Fact]
    public void AttackabilityChangesInvalidatePlacementEvenWithoutMovingTheItem()
    {
        var snapshot = Snapshot();
        var before = PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog");
        snapshot.Inventory!.Items[0].IsAttackable = false;
        var nonAttack = PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog");
        Assert.NotEqual(before, nonAttack);
        snapshot.Inventory.Items[0].IsAttackable = true;
        Assert.NotEqual(nonAttack, PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog"));
    }
    [Fact]
    public void DisabledRecommendationsIgnoreOffersMixerAndPresetFavorites()
    {
        var snapshot = Snapshot();
        var prefs = new PlanPreferences { Recommendations = false };
        var before = PlanFingerprint.Full(snapshot, prefs, "catalog");
        snapshot.Offers.Clear();
        snapshot.Mixer = new MixerState { Cost = 100, Used = true };
        prefs.PresetCharms.Add(999);
        Assert.Equal(before, PlanFingerprint.Full(snapshot, prefs, "catalog"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComboPlacementPrioritiesInvalidatePlacementAndRequests(bool recommendations)
    {
        var snapshot = Snapshot();
        var prefs = new PlanPreferences { Recommendations = recommendations };
        var placement = PlanFingerprint.Placement(snapshot, prefs, "catalog");
        var full = PlanFingerprint.Full(snapshot, prefs, "catalog");
        prefs.PriorityCategories.Add("NEW");
        Assert.NotEqual(placement, PlanFingerprint.Placement(snapshot, prefs, "catalog"));
        Assert.NotEqual(full, PlanFingerprint.Full(snapshot, prefs, "catalog"));
    }

    [Fact]
    public void ReusingPlacementFingerprintPreservesTheFullFingerprint()
    {
        var snapshot = Snapshot();
        var prefs = new PlanPreferences();
        var placement = PlanFingerprint.Placement(snapshot, prefs, "catalog");
        Assert.Equal(PlanFingerprint.Full(snapshot, prefs, "catalog"),
            PlanFingerprint.Full(snapshot, prefs, "catalog", placement));
    }

    [Fact]
    public void CollectionOrderDoesNotChangeTheFingerprint()
    {
        var left = Snapshot();
        var right = Snapshot();
        right.Inventory!.Items.Reverse();
        right.Inventory.LevelMatrix = right.Inventory.LevelMatrix.Reverse()
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal(
            PlanFingerprint.Full(left, PlanPreferences.None, "catalog-1"),
            PlanFingerprint.Full(right, PlanPreferences.None, "catalog-1"));
    }

    [Fact]
    public void WeaponChangesInvalidatePlacement()
    {
        var snapshot = Snapshot();
        var before = PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog-1");

        snapshot.Run!.WeaponId = "Dagger";

        Assert.NotEqual(before, PlanFingerprint.Placement(snapshot, PlanPreferences.None, "catalog-1"));
    }

    [Fact]
    public void RecommendationSettingInvalidatesTheFullPlanOnly()
    {
        var snapshot = Snapshot();
        var enabled = new PlanPreferences { Recommendations = true };
        var disabled = new PlanPreferences { Recommendations = false };

        Assert.NotEqual(
            PlanFingerprint.Full(snapshot, enabled, "catalog-1"),
            PlanFingerprint.Full(snapshot, disabled, "catalog-1"));
        Assert.Equal(
            PlanFingerprint.Placement(snapshot, enabled, "catalog-1"),
            PlanFingerprint.Placement(snapshot, disabled, "catalog-1"));
    }

    [Fact]
    public void ChangedCuratedValuesInvalidatePlacementEvenWhenTheEntryCountIsTheSame()
    {
        var snapshot = Snapshot();
        var low = PreferencesWithValue(1);
        var high = PreferencesWithValue(5);

        Assert.NotEqual(
            PlanFingerprint.Placement(snapshot, low, "catalog-1"),
            PlanFingerprint.Placement(snapshot, high, "catalog-1"));
    }

    /// <summary>
    /// 동전을 줍는 것만으로 판이 다시 풀리면 안 된다. 소지금이 계획에 미치는 영향은 "살 수
    /// 있는가" 하나뿐이므로, 그 답이 그대로면 지문도 그대로여야 한다.
    /// </summary>
    [Fact]
    public void PickingUpGoldWithoutCrossingAPriceKeepsTheFingerprint()
    {
        var snapshot = Snapshot();
        snapshot.Offers.Clear();
        snapshot.Offers.Add(new OfferedItem { DefinitionId = 3, Kind = "charm", SlotIndex = 1, Price = 500 });
        var before = PlanFingerprint.Full(snapshot, PlanPreferences.None, "catalog-1");

        snapshot.Run!.Gold = 143;

        Assert.Equal(before, PlanFingerprint.Full(snapshot, PlanPreferences.None, "catalog-1"));
    }

    [Fact]
    public void ReachingAnOfferPriceInvalidatesTheFullPlan()
    {
        var snapshot = Snapshot();
        snapshot.Offers.Clear();
        snapshot.Offers.Add(new OfferedItem { DefinitionId = 3, Kind = "charm", SlotIndex = 1, Price = 150 });
        var before = PlanFingerprint.Full(snapshot, PlanPreferences.None, "catalog-1");

        snapshot.Run!.Gold = 150;

        Assert.NotEqual(before, PlanFingerprint.Full(snapshot, PlanPreferences.None, "catalog-1"));
    }

    [Fact]
    public void ReachingTheMixerCostInvalidatesTheFullPlan()
    {
        var snapshot = Snapshot();
        snapshot.Mixer = new MixerState { Cost = 150 };
        var before = PlanFingerprint.Full(snapshot, PlanPreferences.None, "catalog-1");

        snapshot.Run!.Gold = 150;

        Assert.NotEqual(before, PlanFingerprint.Full(snapshot, PlanPreferences.None, "catalog-1"));
    }

    /// <summary>
    /// 번호 없는 아이템 둘은 서로 다른 아이템이다. 게임 번호(0)를 그대로 쓰면 지문이 둘을 한
    /// 몸으로 보아, 한쪽이 사라져도 계획이 낡지 않는다.
    /// </summary>
    [Fact]
    public void TwoUnnumberedItemsAreToldApartByTheirCells()
    {
        var one = Snapshot();
        one.Inventory!.Items.Clear();
        one.Inventory.Items.Add(new PlacedItem { DefinitionId = 5007, InstanceId = -1, Position = new GridPos(0, 0), Immovable = true });
        one.Inventory.Items.Add(new PlacedItem { DefinitionId = 5007, InstanceId = -3, Position = new GridPos(2, 0), Immovable = true });

        var moved = Snapshot();
        moved.Inventory!.Items.Clear();
        moved.Inventory.Items.Add(new PlacedItem { DefinitionId = 5007, InstanceId = -1, Position = new GridPos(0, 0), Immovable = true });
        moved.Inventory.Items.Add(new PlacedItem { DefinitionId = 5007, InstanceId = -4, Position = new GridPos(3, 0), Immovable = true });

        Assert.NotEqual(
            PlanFingerprint.Placement(one, PlanPreferences.None, "catalog"),
            PlanFingerprint.Placement(moved, PlanPreferences.None, "catalog"));
    }

    private static PlanPreferences PreferencesWithValue(int tier) => new()
    {
        CharmValues = new CharmValueBook(new CharmValueFile
        {
            Charms =
            {
                new CharmValueEntry { Id = "charm", EntityId = 1, Tier = tier },
            },
        }),
    };

    private static GameSnapshot Snapshot() => new()
    {
        GameVersion = "1",
        Run = new RunState { WeaponId = "Sword", Gold = 100 },
        Inventory = new InventoryState
        {
            Width = 6,
            Height = 7,
            Storage = 6,
            Items =
            {
                new PlacedItem { DefinitionId = 2, InstanceId = 20, IsActive = true },
                new PlacedItem { DefinitionId = 1, InstanceId = 10, IsActive = true },
            },
            LevelMatrix = { ["1,0"] = 2, ["0,0"] = 1 },
        },
        Offers = { new OfferedItem { DefinitionId = 3, Kind = "charm", SlotIndex = 1 } },
    };
}
