using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

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

    /// <summary>
    /// 시나리오 동행 증표는 게임이 인스턴스 번호를 주지 않아 옮길 길이 없다. 제자리에 못 박고
    /// 나머지를 그 주위로 푼다 - 없는 셈 치면 솔버가 그 칸에 다른 것을 놓으라고 하고, 석판
    /// 조건("그 칸에 아티팩트가 있다")도 거짓이 된다.
    /// </summary>
    [Fact]
    public void AnItemThatCannotMoveKeepsItsCellAndTheRestIsPlacedAroundIt()
    {
        var snapshot = Snapshot();

        // 석판이 레벨을 올려 주는 좋은 칸(1,0)에 못 옮기는 아이템이 앉아 있다.
        snapshot.Inventory!.Items[0].Position = new GridPos(2, 0);
        snapshot.Inventory.Items[0].EffectiveLevel = 0;
        snapshot.Inventory.LevelMatrix.Clear();
        snapshot.Inventory.Items.Add(new PlacedItem
        {
            DefinitionId = CharmEntity,
            InstanceId = -8,
            Position = new GridPos(1, 0),
            EffectiveLevel = 2,
            IsActive = true,
            Immovable = true,
        });
        snapshot.Inventory.LevelMatrix["1,0"] = 2;

        var plan = PlanBuilder.Build(snapshot, Catalog())!;

        Assert.Equal(new GridPos(1, 0), plan.Best.CharmPositions[-8]);
        Assert.DoesNotContain(plan.Moves, move => move.From == new GridPos(1, 0) || move.To == new GridPos(1, 0));

        var pinned = plan.Targets.Single(target => target.InstanceId == -8);
        Assert.Equal(pinned.From, pinned.To);
        Assert.True(pinned.Immovable);

        // 석판도 그 칸을 쓸 수 없다.
        Assert.DoesNotContain(plan.Best.TabletPositions.Values, spot => spot.Position == new GridPos(1, 0));

        // 나머지는 그대로 배치된다 - 증표 하나 때문에 계획이 통째로 막히지 않는다.
        Assert.True(plan.Best.CharmPositions.ContainsKey(10));
        Assert.Equal(PlanVerificationStatus.Passed, plan.Verification.Status);
    }

    [Fact]
    public void TheApplyCommandCarriesTheLevelsItExpectsAfterwards()
    {
        // 적용이 끝난 뒤 게임의 레벨과 견줄 잣대다. 이것이 비면 우리가 읽지 않는 효과가 걸려도
        // 첫 적용에서 알아채지 못하고 다음 폴링의 검증까지 기다리게 된다.
        var plan = PlanBuilder.Build(Snapshot(), Catalog());

        var command = plan!.CreateApplyCommand();

        Assert.NotEmpty(command.ExpectedCellLevels);
        Assert.Equal(plan.Best.CellLevels, command.ExpectedCellLevels);
    }

    /// <summary>
    /// 두 단계로 나눠 푼 것이 한 번에 푼 것과 같아야 한다. 화면은 배치를 먼저 받지만 답이
    /// 달라지면 안 된다. <b>조언을 두 번 붙여 본다</b> - 조언이 1단계가 남긴
    /// <c>PlacementProblem</c> 을 고쳐 놓으면 두 번째가 달라진다.
    /// </summary>
    [Fact]
    public void SolvingInTwoStagesGivesTheSameAnswerAsOne()
    {
        var snapshot = Snapshot();
        snapshot.Offers.Add(new OfferedItem { DefinitionId = CharmEntity, Kind = "charm" });
        snapshot.Mixer = new MixerState { Cost = 1 };

        var whole = PlanBuilder.Build(snapshot, Catalog())!;
        var placement = PlanBuilder.BuildPlacement(snapshot, Catalog(), null, out _)!;

        Assert.Equal(AdviceStatus.Pending, placement.AdviceStatus);
        Assert.Empty(placement.Offers);
        Assert.Empty(placement.Mixes);

        var first = PlanBuilder.BuildAdvice(placement, snapshot, Catalog(), null)!;
        var second = PlanBuilder.BuildAdvice(placement, snapshot, Catalog(), null)!;

        Assert.Equal(AdviceStatus.Ready, first.AdviceStatus);
        Assert.NotEmpty(first.Offers);
        Assert.Empty(ReplayResult.From(whole).Differences(ReplayResult.From(first)));
        Assert.Empty(ReplayResult.From(whole).Differences(ReplayResult.From(second)));

        // 넘긴 배치는 그대로 남는다. 게시된 계획을 제자리에서 고치면 화면이 반쯤 채워진 목록을 읽는다.
        Assert.Empty(placement.Offers);
        Assert.Equal(AdviceStatus.Pending, placement.AdviceStatus);
    }

    /// <summary>
    /// 합성 추천은 조언 한 번의 값을 몇 배로 만드는 가장 비싼 계산이다. 층에 합성기가 있기만
    /// 하면 계속 돌던 것을 가까이 갔을 때만 돌린다. 재지 않은 옛 재현 자료(<c>null</c>)는
    /// 전처럼 돌아야 한다 - 거짓으로 읽으면 그 자료들이 합성 추천을 잃는다.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void MixAdviceWaitsUntilTheMixerIsClose(bool? near, bool expected)
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.Tablets.Add(new PlacedTablet
        {
            DefinitionId = TabletEntity,
            InstanceId = 2,
            Position = new GridPos(3, 0),
            IsApplied = true,
        });
        snapshot.Mixer = new MixerState { Cost = 1, Near = near };

        var plan = PlanBuilder.Build(snapshot, Catalog())!;

        Assert.Equal(expected, plan.Mixes.Count > 0);
    }

    /// <summary>
    /// 레벨 제한은 "이 아티팩트는 N레벨까지만 값으로 친다" 는 사용자 선호다. 제한을 걸면 남는
    /// 높은 칸이 다른 아티팩트에게 돌아간다 - ★ 로는 "누가 먼저 갖느냐" 만 말할 수 있고
    /// "얼마나 높은 칸이냐" 는 말할 수 없어서 넣었다.
    /// </summary>
    [Fact]
    public void ALevelCapSendsTheSpareLevelsToAnotherArtifact()
    {
        var catalog = new Catalog(
            new[] { new TabletDefinition { Id = "T", EntityId = TabletEntity, Query = "RIGHT 2" } },
            new[]
            {
                new CharmDefinition { Id = "A", EntityId = CharmEntity, MaxLevel = 5 },
                new CharmDefinition { Id = "B", EntityId = CharmEntity + 1, MaxLevel = 5 },
            });

        // 석판이 (1,0) 만 2레벨로 올린다. 아티팩트 둘 중 하나만 그 칸을 가질 수 있다.
        var snapshot = Snapshot();
        snapshot.Inventory!.Items.Clear();
        snapshot.Inventory.LevelMatrix.Clear();
        snapshot.Inventory.Items.Add(new PlacedItem
        { DefinitionId = CharmEntity, InstanceId = 10, Position = new GridPos(1, 0), EffectiveLevel = 2, IsActive = true });
        snapshot.Inventory.Items.Add(new PlacedItem
        { DefinitionId = CharmEntity + 1, InstanceId = 11, Position = new GridPos(2, 0), IsActive = true });
        snapshot.Inventory.LevelMatrix["1,0"] = 2;

        var free = PlanBuilder.Build(snapshot, catalog)!;
        Assert.Equal(new GridPos(1, 0), free.Best.CharmPositions[10]);

        var capped = PlanBuilder.Build(snapshot, catalog, new PlanPreferences
        {
            LevelCaps = { [CharmEntity] = 1 },
        })!;

        // 1레벨까지만 쳐 주므로 2레벨 칸은 제한이 없는 쪽이 받는다.
        Assert.Equal(new GridPos(1, 0), capped.Best.CharmPositions[11]);
        Assert.NotEqual(new GridPos(1, 0), capped.Best.CharmPositions[10]);
    }

    /// <summary>제한을 걸지 않으면 지문이 그대로여야 한다. 옛 재현 자료가 거부되면 안 된다.</summary>
    [Fact]
    public void ALevelCapOnlyEntersTheFingerprintWhenItIsSet()
    {
        var snapshot = Snapshot();
        var none = PlanFingerprint.Full(snapshot, new PlanPreferences(), "catalog");
        var empty = PlanFingerprint.Full(
            snapshot, new PlanPreferences { LevelCaps = new Dictionary<int, int>() }, "catalog");
        var set = PlanFingerprint.Full(
            snapshot, new PlanPreferences { LevelCaps = { [CharmEntity] = 2 } }, "catalog");

        Assert.Equal(none, empty);
        Assert.NotEqual(none, set);
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
        Assert.Equal(PlanVerificationStatus.Passed, plan.Verification.Status);
        Assert.Equal("C", plan.Names[plan.Best.CharmPositions[10]]);
        Assert.All(plan.Targets, target => Assert.Equal(target.From, target.To));

        var command = plan.CreateApplyCommand();
        Assert.Equal(6, command.ExpectedWidth);
        Assert.Equal(7, command.ExpectedHeight);
        Assert.Equal(6, command.ExpectedStorage);
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
        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void ALevelMissingFromTheSparseMatrixIsComparedAsZero()
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.LevelMatrix.Clear();

        var plan = PlanBuilder.Build(snapshot, Catalog());

        Assert.Equal(PlanVerificationStatus.Failed, plan!.Verification.Status);
        Assert.Equal(1, plan.LevelMismatches);
        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void AnItemEffectiveLevelMismatchIsNotHiddenByTheCellMatrix()
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.Items[0].EffectiveLevel = 4;

        var plan = PlanBuilder.Build(snapshot, Catalog());

        Assert.Equal(0, plan!.LevelMismatches);
        Assert.Equal(1, plan.Verification.EffectiveLevelMismatches);
        Assert.Equal(PlanVerificationStatus.Failed, plan.Verification.Status);
        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void ADisabledCellMismatchLocksAutomaticPlacement()
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.DisabledCells.Add("1,0");
        snapshot.Inventory.Items[0].IsActive = false;

        var plan = PlanBuilder.Build(snapshot, Catalog());

        Assert.Equal(PlanVerificationStatus.Failed, plan!.Verification.Status);
        Assert.True(plan.Verification.DisabledMismatches > 0);
        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void ATabletApplicationMismatchLocksAutomaticPlacement()
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.Tablets[0].IsApplied = false;

        var plan = PlanBuilder.Build(snapshot, Catalog());

        Assert.Equal(PlanVerificationStatus.Failed, plan!.Verification.Status);
        Assert.Equal(1, plan.Verification.TabletMismatches);
        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void AMatrixTheGameHasNotRecalculatedWaitsInsteadOfFailing()
    {
        var snapshot = Snapshot();
        snapshot.Inventory!.LevelMatrix.Clear();
        snapshot.Inventory.Tablets[0].AppliedRangeStale = true;

        var plan = PlanBuilder.Build(snapshot, Catalog());

        Assert.Equal(PlanVerificationStatus.Unavailable, plan!.Verification.Status);
        Assert.Equal(PlanVerification.GameNotRecalculated, plan.Verification.Reason);
        Assert.Empty(plan.Targets);
    }

    [Fact]
    public void AStaleTabletChangesThePlacementFingerprintOnlyWhenPresent()
    {
        var snapshot = Snapshot();
        var fresh = PlanFingerprint.Placement(snapshot, "test");

        snapshot.Inventory!.Tablets[0].AppliedRangeStale = true;
        Assert.NotEqual(fresh, PlanFingerprint.Placement(snapshot, "test"));

        snapshot.Inventory.Tablets[0].AppliedRangeStale = false;
        Assert.Equal(fresh, PlanFingerprint.Placement(snapshot, "test"));
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
