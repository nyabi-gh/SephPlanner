using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 제보 9ba3e393: 플라즈마 빌드인데 게임이 넣어 준 오른편(얼음)에 천칭이 고정됐다. 새로 들어와 아직
/// 옮기지 않은 천칭만 우선 콤보를 따르고, 한 번 옮겨진 천칭은 예전처럼 놓인 편을 지킨다.
/// </summary>
public class ScalesArrivalTests
{
    private const int ScalesId = 7;
    private static readonly GridPos Dropped = new(4, 0);

    private static Catalog Catalog() => new(Array.Empty<TabletDefinition>(), new[]
    {
        new CharmDefinition { Id = "A", EntityId = 1, MaxLevel = 5 },
        new CharmDefinition { Id = "FireIce", EntityId = 1144, Behavior = "Charm_FireIce", MaxLevel = 5 },
        new CharmDefinition { Id = "FireIceWeapon", EntityId = 1259, Behavior = "Charm_FireIceWeapon", MaxLevel = 5 },
    });

    /// <summary>게임이 천칭을 넣어 준 오른편 칸이 가장 높다 - 점수만 보면 그 자리를 지킨다.</summary>
    private static GameSnapshot Snapshot()
    {
        var inventory = new InventoryState { Width = 6, Height = 1, Storage = 6 };
        inventory.Items.Add(new PlacedItem { DefinitionId = 1, InstanceId = 10, Position = new GridPos(1, 0), IsActive = true });
        inventory.FixedEffects.Add(new FixedEffectCell { Position = Dropped, Level = 3 });
        inventory.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Level = 1 });
        inventory.LevelMatrix["4,0"] = 3;
        inventory.LevelMatrix["0,0"] = 1;
        return new GameSnapshot { Inventory = inventory };
    }

    private static PlacedItem Scales(int definition = 1144) => new()
    {
        DefinitionId = definition,
        InstanceId = ScalesId,
        Position = Dropped,
        EffectiveLevel = 3,
        IsActive = true,
    };

    private static PlanPreferences Priority(params string[] categories) =>
        new() { Recommendations = false, PriorityCategories = new HashSet<string>(categories) };

    private static GridPos Planned(GameSnapshot snapshot, PlanPreferences preferences) =>
        PlanBuilder.Build(snapshot, Catalog(), preferences)!.Best.CharmPositions[ScalesId];

    [Theory]
    [InlineData(true, "EMBER")]
    [InlineData(true, "MAGITECH", "EMBER")]
    [InlineData(false, "GLACIER")]
    [InlineData(false, "EMBER", "GLACIER")]
    [InlineData(false)]
    public void ANewScalesGoesToThePrioritySideOrStaysWhenThereIsNone(bool left, params string[] priority)
    {
        var snapshot = Snapshot();
        var scales = Scales();
        scales.Arrived = true;
        snapshot.Inventory!.Items.Add(scales);

        var planned = Planned(snapshot, Priority(priority));

        Assert.Equal(left, ScalesPosition.IsLeft(planned));
        if (!left) Assert.Equal(Dropped, planned);
    }

    [Fact]
    public void AScalesTheUserAlreadyHadKeepsItsSideWhateverThePriority()
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.Items.Add(Scales());

        Assert.Equal(Dropped, Planned(snapshot, Priority("EMBER")));
    }

    [Fact]
    public void EternalFormulaDoesNotFollowThePriority()
    {
        var snapshot = Snapshot();
        var formula = Scales(1259);
        formula.Arrived = true;
        snapshot.Inventory!.Items.Add(formula);

        Assert.Equal(Dropped, Planned(snapshot, Priority("EMBER")));
    }

    [Fact]
    public void OnceMovedTheScalesKeepsWhereverItWasMovedTo()
    {
        var arrivals = new ItemArrivals();
        var snapshot = Snapshot();
        arrivals.Mark(snapshot.Inventory);
        var scales = Scales();
        snapshot.Inventory!.Items.Add(scales);
        arrivals.Mark(snapshot.Inventory);
        var ember = Priority("EMBER");

        var target = Planned(snapshot, ember);
        Assert.True(ScalesPosition.IsLeft(target));

        scales.Position = target;
        arrivals.Mark(snapshot.Inventory);
        Assert.False(scales.Arrived);

        // 사용자가 도로 오른편에 두면 그것이 사용자의 편이다.
        scales.Position = new GridPos(5, 0);
        arrivals.Mark(snapshot.Inventory);
        Assert.False(scales.Arrived);
        Assert.False(ScalesPosition.IsLeft(Planned(snapshot, ember)));
    }

    [Fact]
    public void OnlyItemsThatCameInDuringTheSessionAndStayPutAreArrivals()
    {
        var arrivals = new ItemArrivals();
        arrivals.Mark(null);
        var snapshot = Snapshot();
        var existing = snapshot.Inventory!.Items[0];
        arrivals.Mark(snapshot.Inventory);
        Assert.False(existing.Arrived);

        var scales = Scales();
        var companion = new PlacedItem { DefinitionId = 1, InstanceId = -3, Position = new GridPos(2, 0), Immovable = true };
        snapshot.Inventory.Items.Add(scales);
        snapshot.Inventory.Items.Add(companion);
        arrivals.Mark(snapshot.Inventory);
        arrivals.Mark(snapshot.Inventory);
        Assert.True(scales.Arrived);
        Assert.False(companion.Arrived);
        Assert.False(existing.Arrived);

        scales.Position = new GridPos(5, 0);
        arrivals.Mark(snapshot.Inventory);
        scales.Position = Dropped;
        arrivals.Mark(snapshot.Inventory);
        Assert.False(scales.Arrived);

        var other = Scales();
        other.InstanceId = 8;
        snapshot.Inventory.Items.Add(other);
        arrivals.Reset();
        arrivals.Mark(snapshot.Inventory);
        Assert.False(other.Arrived);
    }

    [Fact]
    public void AnArrivalChangesThePlacementFingerprint()
    {
        var snapshot = Snapshot();
        var scales = Scales();
        snapshot.Inventory!.Items.Add(scales);
        var preferences = Priority("EMBER");
        var before = PlanFingerprint.Placement(snapshot, preferences, "generation");

        scales.Arrived = true;

        Assert.NotEqual(before, PlanFingerprint.Placement(snapshot, preferences, "generation"));
    }
}
