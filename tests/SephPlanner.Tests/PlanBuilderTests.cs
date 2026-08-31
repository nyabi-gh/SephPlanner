using SephPlanner.Core.Charms;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;

namespace SephPlanner.Tests;

/// <summary>
/// 스냅샷의 값이 솔버까지 제대로 전달되는지 본다. 솔버가 아무리 맞아도 여기서 흘리면
/// 화면에는 틀린 값이 나온다.
/// </summary>
public class PlanBuilderTests
{
    private const int TabletEntity = 100;
    private const int CharmEntity = 200;

    /// <summary>자기 오른쪽 칸의 레벨을 2 올리는 석판 하나와 평범한 아티팩트 하나.</summary>
    private static Catalog Catalog(CharmDefinition? charm = null) => new(
        new[] { new TabletDefinition { Id = "T", EntityId = TabletEntity, Query = "RIGHT 2" } },
        new[] { charm ?? new CharmDefinition { Id = "C", EntityId = CharmEntity, MaxLevel = 5 } });

    private static GameSnapshot Snapshot(
        int enchant = 0, int reportedLevel = 2, string weapon = "", int gold = 1000)
    {
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 6 };

        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = TabletEntity,
            InstanceId = 1,
            Position = new GridPos(0, 0),
            IsApplied = true,
        });

        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = CharmEntity,
            InstanceId = 10,
            Position = new GridPos(1, 0),
            EffectiveLevel = reportedLevel,
            IsActive = true,
            Enchant = enchant,
        });

        inventory.LevelMatrix["1,0"] = reportedLevel;

        return new GameSnapshot
        {
            Inventory = inventory,
            Run = new RunState { WeaponId = weapon, Gold = gold },
        };
    }

    [Fact]
    public void TurningRecommendationsOffSkipsOffersButKeepsPlacement()
    {
        var snapshot = Snapshot();
        snapshot.Offers.Add(new OfferedItem { DefinitionId = CharmEntity, Kind = "charm" });

        var quiet = PlanBuilder.Build(snapshot, Catalog(), new PlanPreferences { Recommendations = false });
        var full = PlanBuilder.Build(snapshot, Catalog());

        Assert.NotNull(quiet);
        Assert.Empty(quiet!.Offers);
        Assert.NotEmpty(full!.Offers);
        Assert.Equal(full.Best.Score, quiet.Best.Score);
    }

    [Fact]
    public void ASnapshotBecomesAScoredPlan()
    {
        var plan = PlanBuilder.Build(Snapshot(), Catalog());

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.Current.Score, 3);
        Assert.Equal(0, plan.LevelMismatches);
        Assert.Equal("C", plan.Names[plan.Best.CharmPositions[10]]);
    }

    [Fact]
    public void TheEnchantFromTheSnapshotReachesTheScore()
    {
        // 인챈트 2가 붙어 게임이 4로 보고한 상황. 우리 계산도 4가 되어야 어긋나지 않는다.
        var plan = PlanBuilder.Build(Snapshot(enchant: 2, reportedLevel: 4), Catalog());

        Assert.Equal(5, plan!.Current.Score, 3);
        Assert.Equal(0, plan.LevelMismatches);
    }

    [Fact]
    public void LevelsThatDisagreeWithTheGameAreCounted()
    {
        // 게임은 4라는데 우리가 아는 근거는 석판 몫 2뿐이다. 읽지 못한 효과가 있다는 뜻이다.
        var plan = PlanBuilder.Build(Snapshot(enchant: 0, reportedLevel: 4), Catalog());

        Assert.Equal(1, plan!.LevelMismatches);
    }

    [Fact]
    public void AWeaponMismatchFromTheSnapshotTurnsTheCharmOff()
    {
        var charm = new CharmDefinition
        {
            Id = "C",
            EntityId = CharmEntity,
            MaxLevel = 5,
            IsWeaponRelated = true,
            RelatedWeapon = "GreatSword",
        };

        var plan = PlanBuilder.Build(Snapshot(weapon: "Dagger"), Catalog(charm));

        Assert.Equal(0, plan!.Best.Score, 3);
        Assert.Equal(CharmInactiveReason.Weapon, plan.Best.InactiveCells[plan.Best.CharmPositions[10]]);
    }

    [Fact]
    public void TheSameCharmStaysOnWhenTheWeaponMatches()
    {
        var charm = new CharmDefinition
        {
            Id = "C",
            EntityId = CharmEntity,
            MaxLevel = 5,
            IsWeaponRelated = true,
            RelatedWeapon = "GreatSword",
        };

        var plan = PlanBuilder.Build(Snapshot(weapon: "GreatSword"), Catalog(charm));

        Assert.Equal(3, plan!.Best.Score, 3);
        Assert.Empty(plan.Best.InactiveCells);
    }

    [Fact]
    public void AnItemMissingFromTheCatalogStillTakesUpItsCell()
    {
        // 소모품처럼 카탈로그에 없는 것을 빼놓으면 솔버가 그 자리를 비었다고 보고 거기로 옮기라고 한다.
        var snapshot = Snapshot();
        snapshot.Inventory!.Items.Add(new PlacedItem
        {
            DefinitionId = 9999,
            InstanceId = 11,
            Position = new GridPos(2, 0),
            IsActive = true,
        });

        var plan = PlanBuilder.Build(snapshot, Catalog());

        Assert.True(plan!.Best.CharmPositions.ContainsKey(11));
        Assert.NotEqual(plan.Best.CharmPositions[10], plan.Best.CharmPositions[11]);
    }

    [Fact]
    public void ThereIsNothingToPlanWithoutARun()
    {
        Assert.Null(PlanBuilder.Build(new GameSnapshot(), Catalog(), null, out var blocker));
        Assert.Equal(PlanBlocker.NoInventory, blocker);
    }

    [Fact]
    public void ThereIsNothingToPlanWhenNoItemIsAnArtifact()
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.Items[0].DefinitionId = 9999;

        Assert.Null(PlanBuilder.Build(snapshot, Catalog(), null, out var blocker));

        // 물건은 격자에 있다. 비어 있는 것과 구별돼야 화면이 "데이터를 다시 만들라"고 말할 수 있다.
        Assert.Equal(PlanBlocker.UnknownItems, blocker);
    }

    /// <summary>
    /// 탐험을 막 시작하면 격자가 비어 있다. 이때 답이 없는 것을 "계산 중"으로 보여 주면 영영
    /// 계산만 하는 것처럼 보이므로, 빈 격자임을 화면이 알 수 있어야 한다.
    /// </summary>
    [Fact]
    public void AnEmptyGridSaysSoInsteadOfLookingLikeAStalledSolve()
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.Items.Clear();

        Assert.Null(PlanBuilder.Build(snapshot, Catalog(), null, out var blocker));
        Assert.Equal(PlanBlocker.NoCharms, blocker);
    }
}
