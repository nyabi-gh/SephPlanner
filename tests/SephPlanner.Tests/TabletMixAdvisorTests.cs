using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 석판 합성기 추천. 합성은 재료 둘을 없애고 결과 하나를 만들며, 합성 시점의 회전이 결과 질의에
/// 구워진다. 그래서 "무엇과 무엇을"만이 아니라 "어느 회전으로"까지가 답의 일부다.
/// </summary>
public class TabletMixAdvisorTests
{
    private static readonly GridSpec Grid = new(6, 7, 12);

    private static MixMaterial Material(
        int instanceId, string query, string condition = "", bool rotatable = true, int rotation = 0) =>
        new(instanceId, entityId: 100 + instanceId, query, condition, rotatable, rotation);

    [Fact]
    public void RotatingAQueryIsTheSameAsReadingItRotated()
    {
        // 합성이 회전을 문자열에 구워 넣으므로, 구운 것을 읽은 결과가 원본을 돌려 읽은 것과
        // 같아야 한다. 어긋나면 합성 결과의 자리가 게임과 달라진다.
        const string query = "RIGHT 2\nUP 1\nKNIGHTUPLEFT 3\nHORIZONTAL 1\nO 4";
        var origin = new GridPos(2, 3);

        for (var baked = 0; baked < 4; baked++)
        {
            for (var read = 0; read < 4; read++)
            {
                var viaString = TabletQuery.Parse(TabletQuery.Rotated(query, baked), Grid, origin, read);
                var viaParse = TabletQuery.Parse(query, Grid, origin, baked + read);

                Assert.Equal(
                    viaParse.Select(cell => (cell.Position, cell.Value)).OrderBy(c => c.Position.X).ThenBy(c => c.Position.Y),
                    viaString.Select(cell => (cell.Position, cell.Value)).OrderBy(c => c.Position.X).ThenBy(c => c.Position.Y));
            }
        }
    }

    [Fact]
    public void TheMixedQueryIsBothQueriesBakedAtTheirRotations()
    {
        var mixed = TabletMix.Of(Material(1, "RIGHT 2"), 0, Material(2, "RIGHT 3"), 1);

        Assert.NotNull(mixed);
        // 회전은 (x,y) -> (y,-x) 이므로 RIGHT(1,0) 를 한 번 돌리면 UP(0,-1) 이다.
        // 좌표가 아니라 토큰 이름으로 나와야 한다 - 게임도 이름을 갈아 끼운다.
        Assert.Equal("RIGHT 2\nUP 3", mixed!.Query);
    }

    [Fact]
    public void TwoTabletsWithDifferentConditionsCannotBeMixed()
    {
        var mixed = TabletMix.Of(
            Material(1, "RIGHT 2", condition: "O CHARM"), 0,
            Material(2, "RIGHT 3", condition: "O MAGIC"), 0);

        Assert.Null(mixed);
    }

    [Fact]
    public void OneSidedConditionsPassThroughInsteadOfBlocking()
    {
        // 게임은 양쪽이 다 있을 때만 같은지 따진다. 한쪽이 비었으면 있는 쪽이 결과의 조건이 된다.
        var mixed = TabletMix.Of(
            Material(1, "RIGHT 2", condition: "O CHARM"), 0,
            Material(2, "RIGHT 3"), 0);

        Assert.NotNull(mixed);
        Assert.Equal("O CHARM", mixed!.ConditionQuery);
    }

    [Fact]
    public void AMixedTabletCannotBeMixedAgain()
    {
        var already = new MixMaterial(1, TabletMix.ResultEntityId, "RIGHT 2", "", rotatable: true);

        Assert.Null(TabletMix.Of(already, 0, Material(2, "RIGHT 3"), 0));
    }

    [Fact]
    public void TheResultTurnsOnlyIfBothMaterialsCould()
    {
        var free = Material(1, "RIGHT 2");
        var locked = Material(2, "RIGHT 3", rotatable: false);

        Assert.True(TabletMix.Of(free, 0, Material(3, "UP 1"), 0)!.Rotatable);
        Assert.False(TabletMix.Of(free, 0, locked, 0)!.Rotatable);
    }

    [Fact]
    public void ALockedMaterialAlwaysMixesUnturnedWhateverItsBagAngle()
    {
        // 합성 창의 아이콘은 돌릴 수 없는 석판의 회전을 0 으로 되돌린다(UI_ItemIcon.UpdateIcon).
        var locked = Material(2, "RIGHT 3", rotatable: false, rotation: 1);

        Assert.NotNull(TabletMix.Of(Material(1, "RIGHT 2"), 0, locked, 0));
        Assert.Null(TabletMix.Of(Material(1, "RIGHT 2"), 0, locked, 1));
        Assert.Equal(new[] { 0 }, TabletMix.Rotations(locked));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 1, 1, 0)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(0, 2, 0, 2)]
    public void TurnsCountFromTheAngleEachTabletSitsAtInTheBag(int bagA, int bagB, int turnsA, int turnsB)
    {
        // 제자리 질의는 어느 회전이든 결과가 같아 사이 각도 0 이 뽑힌다. 창은 가방 방향으로
        // 받으므로, 가방에서 이미 어긋나 있으면 그만큼 돌려 맞춰야 하고 덜 누르는 쪽을 돌린다.
        var advice = Assert.Single(TabletMixAdvisor.Rank(Bag(("A", bagA), ("B", bagB)), new Catalog([], []), 0, 100));

        Assert.Equal(0, advice.RotationB - advice.RotationA);
        Assert.Equal(turnsA, advice.TurnsA);
        Assert.Equal(turnsB, advice.TurnsB);
        Assert.False(advice.AnyOfSameKind);
    }

    [Fact]
    public void TwoOfTheSameTabletGiveOneRowNotTwo()
    {
        var advice = TabletMixAdvisor.Rank(Bag(("A", 0), ("B", 0), ("B", 0)), new Catalog([], []), 0, 100);

        var mix = Assert.Single(advice, entry => entry.NameA == "A" && entry.NameB == "B");
        Assert.True(mix.AnyOfSameKind);
        Assert.False(mix.TurnsDependOnPick);
        Assert.Contains("어느 것을 넣어도", Explain.Join(Explain.Mix(mix)));
    }

    [Fact]
    public void SameTabletsFacingDifferentWaysAreDescribedByShapeNotClicks()
    {
        var advice = TabletMixAdvisor.Rank(Bag(("A", 0), ("B", 0), ("B", 1)), new Catalog([], []), 0, 100);

        var mix = Assert.Single(advice, entry => entry.NameA == "A" && entry.NameB == "B");
        Assert.True(mix.TurnsDependOnPick);
        Assert.Equal("회전 확인", Explain.TurnTag(mix));
        Assert.Contains("A과(와) B이(가) 같은 방향이 되게 맞추세요", Explain.Turn(mix));
        Assert.DoesNotContain("0번", Explain.Turn(mix));
    }

    /// <summary>
    /// 같은 석판 둘은 합성 창에서 어느 쪽이 어느 것인지 짚을 수 없다. 한쪽을 한 번 돌리면 같은
    /// 방향이 되고 다른 쪽을 한 번 돌리면 반대 방향이 되므로, "B 를 1번" 은 반쯤 틀린 안내였다
    /// (제보 dcd4ef12 의 쌍성 + 쌍성).
    /// </summary>
    [Fact]
    public void TwoOfTheSameTabletAreToldByTheirAngleNotClicks()
    {
        var turned = Assert.Single(TabletMixAdvisor.Rank(Bag(("B", 0), ("B", 1)), new Catalog([], []), 0, 100));
        Assert.True(turned.SameKind);
        Assert.Equal("회전 확인", Explain.TurnTag(turned));
        Assert.Contains("두 B이(가) 같은 방향이 되게", Explain.Turn(turned));
        Assert.DoesNotContain("번 돌린", Explain.Turn(turned));
        Assert.StartsWith("B 둘을 합칩니다", Explain.Mix(turned)[0]);

        var aligned = Assert.Single(TabletMixAdvisor.Rank(Bag(("B", 1), ("B", 1)), new Catalog([], []), 0, 100));
        Assert.Equal("", Explain.TurnTag(aligned));
        Assert.Contains("그대로 합치면", Explain.Turn(aligned));
    }

    [Fact]
    public void AShapeWithoutTurnsIsCalledTheUnturnedShape()
    {
        var advice = new MixAdvice
        {
            NameA = "쌍성",
            NameB = "동시성",
            RotatableA = true,
            RotatableB = false,
            TurnsDependOnPick = true,
        };

        Assert.Contains("쌍성이 돌리지 않은 처음 모양이 되게", Explain.Turn(advice));
        Assert.DoesNotContain("0번", Explain.Turn(advice));
    }

    [Theory]
    [InlineData("쌍성", "쌍성과", "쌍성을", "쌍성이")]
    [InlineData("방어수", "방어수와", "방어수를", "방어수가")]
    [InlineData("A", "A과(와)", "A을(를)", "A이(가)")]
    public void ParticlesFollowTheLastSyllable(string name, string and, string obj, string subject)
    {
        Assert.Equal(and, Explain.With(name, "과", "와"));
        Assert.Equal(obj, Explain.With(name, "을", "를"));
        Assert.Equal(subject, Explain.With(name, "이", "가"));
    }

    private static PlacementProblem Bag(params (string Name, int Rotation)[] tablets)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(4, 4, 16) };
        for (var id = 1; id <= 6; id++)
        {
            problem.Charms.Add(new CharmSlot { InstanceId = id });
            problem.CurrentCharms[id] = problem.Grid.ToPosition(id - 1);
        }
        for (var i = 0; i < tablets.Length; i++)
        {
            var (name, rotation) = tablets[i];
            var instance = 100 + i;
            problem.Tablets.Add(new TabletSlot
            {
                InstanceId = instance,
                Rotatable = true,
                Definition = new TabletDefinition
                {
                    EntityId = name == "A" ? 300 : 301,
                    Id = name,
                    Query = name == "A" ? "O 1" : "O 2",
                    Names = { ["current"] = name },
                },
            });
            problem.CurrentTablets[instance] = new TabletSpot(problem.Grid.ToPosition(8 + i), rotation);
        }
        return problem;
    }

    [Fact]
    public void MixingIsOfferedOnlyWhereTheMixerIs()
    {
        var withoutMixer = PlanBuilder.Build(Snapshot(mixer: null), Catalog());
        var withMixer = PlanBuilder.Build(Snapshot(new MixerState { Cost = 200 }), Catalog());

        Assert.Empty(withoutMixer!.Mixes);
        Assert.NotEmpty(withMixer!.Mixes);
    }

    [Fact]
    public void AMixerAlreadyUsedThisFloorAdvisesNothing()
    {
        var plan = PlanBuilder.Build(Snapshot(new MixerState { Cost = 200, Used = true }), Catalog());

        Assert.Empty(plan!.Mixes);
    }

    [Fact]
    public void TurningRecommendationsOffSkipsMixingToo()
    {
        // 무엇을 합칠지는 판단을 빌려주는 조언이다. 배치(정렬)와 달리 추천 끄기의 지배를 받는다.
        var plan = PlanBuilder.Build(
            Snapshot(new MixerState { Cost = 200 }), Catalog(),
            new PlanPreferences { Recommendations = false });

        Assert.Empty(plan!.Mixes);
    }

    [Fact]
    public void TheAdviceSaysWhichTwoAndAtWhatAngle()
    {
        var plan = PlanBuilder.Build(Snapshot(new MixerState { Cost = 200 }), Catalog());
        var best = plan!.Mixes[0];

        Assert.NotEqual(best.InstanceA, best.InstanceB);
        Assert.InRange(best.RotationA, 0, 3);
        Assert.InRange(best.RotationB, 0, 3);
        Assert.NotEmpty(best.NameA);
        Assert.Contains("\n", best.Query);
    }

    [Fact]
    public void WhatCannotBeAffordedIsStillShownButMarked()
    {
        var plan = PlanBuilder.Build(Snapshot(new MixerState { Cost = 200 }, gold: 10), Catalog());

        Assert.NotEmpty(plan!.Mixes);
        Assert.All(plan.Mixes, entry => Assert.False(entry.Affordable));
    }

    [Theory]
    [InlineData(12000)]
    [InlineData(12002)]
    public void NonDiscardableTabletsStayInTheLayoutButNeverBecomeMixMaterials(int entityId)
    {
        var catalog = Catalog().Export();
        var curse = new TabletDefinition
        {
            Id = "Curse",
            EntityId = entityId,
            IsRotatable = true,
            Query = "CHECKERBOARD2 1\nCHECKERBOARD -1",
            Names = { ["current"] = "저주" },
        };
        catalog.Tablets!.Add(curse);
        var snapshot = Snapshot(new MixerState { Cost = 200 });
        snapshot.Inventory!.Tablets.Add(new PlacedTablet
        {
            DefinitionId = entityId,
            InstanceId = 3,
            Position = new GridPos(4, 0),
            IsApplied = true,
            IsRotatable = true,
        });
        var unrestricted = PlanBuilder.Build(snapshot, catalog.Restore())!;
        Assert.Contains(unrestricted.Mixes, mix => mix.InstanceA == 3 || mix.InstanceB == 3);
        foreach (var cell in unrestricted.Current.CellLevels)
            snapshot.Inventory.LevelMatrix[$"{cell.Key.X},{cell.Key.Y}"] = cell.Value;
        foreach (var item in snapshot.Inventory.Items)
        {
            item.EffectiveLevel = unrestricted.Current.Levels[item.Position];
            item.IsActive = !unrestricted.Current.DisabledCells.Contains(item.Position);
        }
        foreach (var cell in unrestricted.Current.DisabledCells)
            snapshot.Inventory.DisabledCells.Add($"{cell.X},{cell.Y}");
        foreach (var tablet in snapshot.Inventory.Tablets)
            tablet.IsApplied = unrestricted.Current.AppliedTablets[tablet.InstanceId];

        curse.CannotDiscard = true;
        var restored = System.Text.Json.JsonSerializer.Deserialize<ReplayCatalog>(
            Newtonsoft.Json.JsonConvert.SerializeObject(catalog))!;
        var restricted = PlanBuilder.Build(snapshot, restored.Restore())!;

        var advice = Assert.Single(restricted.Mixes);
        Assert.Equal(1, advice.InstanceA);
        Assert.Equal(2, advice.InstanceB);
        Assert.True(restricted.Verification.Passed, Newtonsoft.Json.JsonConvert.SerializeObject(restricted.Verification));
        Assert.Contains(restricted.Targets, target => target.IsTablet && target.InstanceId == 3);
        Assert.Contains(restricted.Best.Tablets, tablet => tablet.Definition.EntityId == entityId);
        Assert.Equal(unrestricted.Current.Score, restricted.Current.Score);
        Assert.Equal(unrestricted.Best.Score, restricted.Best.Score);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(301)]
    public void OneNonDiscardableMaterialLeavesNoMixablePair(int entityId)
    {
        var catalog = Catalog();
        catalog.Tablet(entityId)!.CannotDiscard = true;

        var plan = PlanBuilder.Build(Snapshot(new MixerState { Cost = 200 }), catalog)!;

        Assert.Empty(plan.Mixes);
        Assert.Equal(2, plan.Best.Tablets.Count);
    }

    private static Catalog Catalog() => new(
        new[]
        {
            new TabletDefinition { Id = "Left", EntityId = 300, IsRotatable = true, Query = "LEFT 2", Names = { ["current"] = "왼쪽" } },
            new TabletDefinition { Id = "Right", EntityId = 301, IsRotatable = true, Query = "RIGHT 2", Names = { ["current"] = "오른쪽" } },
            new TabletDefinition { Id = "Mixed", EntityId = TabletMix.ResultEntityId, IsRotatable = false, Query = "", Names = { ["current"] = "..." } },
        },
        new[]
        {
            new CharmDefinition { Id = "A", EntityId = 200, MaxLevel = 5 },
            new CharmDefinition { Id = "B", EntityId = 201, MaxLevel = 5 },
        });

    private static GameSnapshot Snapshot(MixerState? mixer, int gold = 1000)
    {
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 12 };

        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = 300,
            InstanceId = 1,
            Position = new GridPos(0, 0),
            IsApplied = true,
            IsRotatable = true,
        });
        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = 301,
            InstanceId = 2,
            Position = new GridPos(3, 0),
            IsApplied = true,
            IsRotatable = true,
        });

        inventory.Items.Add(new PlacedItem { DefinitionId = 200, InstanceId = 10, Position = new GridPos(1, 0) });
        inventory.Items.Add(new PlacedItem { DefinitionId = 201, InstanceId = 11, Position = new GridPos(2, 0) });

        return new GameSnapshot
        {
            Inventory = inventory,
            Mixer = mixer,
            Run = new RunState { Gold = gold },
        };
    }
}
