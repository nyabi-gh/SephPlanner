using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public class PlanFingerprintTests
{
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
