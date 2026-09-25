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

    private static bool Sided(PlacedItem item) => item.DefinitionId == 1144;

    [Fact]
    public void OnceMovedTheScalesKeepsWhereverItWasMovedTo()
    {
        var arrivals = new ItemArrivals();
        var snapshot = Snapshot();
        arrivals.Mark(snapshot.Inventory, Sided);
        var scales = Scales();
        snapshot.Inventory!.Items.Add(scales);
        arrivals.Mark(snapshot.Inventory, Sided);
        var ember = Priority("EMBER");

        var target = Planned(snapshot, ember);
        Assert.True(ScalesPosition.IsLeft(target));

        scales.Position = target;
        arrivals.Mark(snapshot.Inventory, Sided);
        Assert.False(scales.Arrived);

        // 사용자가 도로 오른편에 두면 그것이 사용자의 편이다.
        scales.Position = new GridPos(5, 0);
        arrivals.Mark(snapshot.Inventory, Sided);
        Assert.False(scales.Arrived);
        Assert.False(ScalesPosition.IsLeft(Planned(snapshot, ember)));
    }

    [Fact]
    public void OnlyTrackedItemsThatCameInDuringTheSessionAndStayPutAreArrivals()
    {
        var arrivals = new ItemArrivals();
        arrivals.Mark(null, Sided);
        var snapshot = Snapshot();
        var existing = snapshot.Inventory!.Items[0];
        arrivals.Mark(snapshot.Inventory, Sided);

        var scales = Scales();
        var picked = new PlacedItem { DefinitionId = 1, InstanceId = 11, Position = new GridPos(2, 0) };
        var companion = new PlacedItem { DefinitionId = 1144, InstanceId = -3, Position = new GridPos(3, 0), Immovable = true };
        snapshot.Inventory.Items.AddRange(new[] { scales, picked, companion });
        arrivals.Mark(snapshot.Inventory, Sided);
        arrivals.Mark(snapshot.Inventory, Sided);
        Assert.True(scales.Arrived);
        Assert.False(picked.Arrived);
        Assert.False(companion.Arrived);
        Assert.False(existing.Arrived);

        scales.Position = new GridPos(5, 0);
        arrivals.Mark(snapshot.Inventory, Sided);
        scales.Position = Dropped;
        arrivals.Mark(snapshot.Inventory, Sided);
        Assert.False(scales.Arrived);
    }

    /// <summary>
    /// 저장된 판은 아바타가 빈 가방으로 생긴 뒤에 채워진다. 그 빈 가방을 기준으로 삼으면 사용자가 전 판에
    /// 둔 천칭까지 새로 들어온 것이 된다.
    /// </summary>
    [Fact]
    public void AnEmptyBagBeforeTheSaveIsRestoredIsNotTheBaseline()
    {
        var arrivals = new ItemArrivals();
        arrivals.Mark(new InventoryState { Width = 6, Height = 1, Storage = 6 }, Sided);

        var restored = Snapshot();
        var scales = Scales();
        restored.Inventory!.Items.Add(scales);
        arrivals.Mark(restored.Inventory, Sided);

        Assert.False(scales.Arrived);
        Assert.Equal(Dropped, Planned(restored, Priority("EMBER")));

        var picked = Scales();
        picked.InstanceId = 8;
        picked.Position = new GridPos(5, 0);
        restored.Inventory.Items.Add(picked);
        arrivals.Mark(restored.Inventory, Sided);
        Assert.True(picked.Arrived);

        arrivals.Reset();
        arrivals.Mark(restored.Inventory, Sided);
        Assert.False(picked.Arrived);
    }

    /// <summary>
    /// 자동 배치가 새 천칭을 옮기지 않았으면 적용 뒤에도 새로 들어온 채다. 예상 판이 그것을 빠뜨리면
    /// "계획 그대로"를 알아보지 못해 처음부터 다시 푼다.
    /// </summary>
    [Fact]
    public void AnArrivalTheApplyDidNotMoveStillCountsAsThePlannedLayout()
    {
        var before = Snapshot();
        var scales = Scales();
        scales.Position = new GridPos(0, 0);
        scales.EffectiveLevel = 1;
        scales.Arrived = true;
        before.Inventory!.Items.Add(scales);
        var preferences = Priority("EMBER");
        var plan = PlanBuilder.Build(before, Catalog(), preferences)!;
        var context = PlanFingerprint.PlanningContext(preferences, "generation");
        plan.PlanningContextFingerprint = context;
        Assert.Equal(scales.Position, plan.Best.CharmPositions[ScalesId]);

        var after = Snapshot();
        after.Inventory!.Items.Clear();
        foreach (var item in before.Inventory.Items)
            after.Inventory.Items.Add(new PlacedItem
            {
                DefinitionId = item.DefinitionId,
                InstanceId = item.InstanceId,
                Position = plan.Best.CharmPositions[item.InstanceId],
                IsActive = true,
                Arrived = item.Arrived && plan.Best.CharmPositions[item.InstanceId] == item.Position,
            });

        Assert.True(AppliedPlacement.Settled(
            plan, before, after, PlanFingerprint.Placement(after, context), context));
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
