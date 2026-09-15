using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class EnchantAdvisorTests
{
    /// <summary>
    /// 칸 하나에 아티팩트 하나. 배치가 흔들릴 자리가 없어야 인챈트 한 단계의 값만 남는다.
    /// </summary>
    private static PlacementProblem OneCell(
        int cellLevel = 0, int multiply = 0, int maxLevel = 5, int enchant = 0, double weight = 1)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 1, 1) };
        if (cellLevel != 0 || multiply != 0)
            problem.FixedEffects.Add(new FixedEffectCell
            {
                Position = new GridPos(0, 0),
                Level = cellLevel,
                Multiply = multiply,
            });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Enchant = enchant,
            Weight = weight,
            Definition = new CharmDefinition { EntityId = 1, Id = "대상", MaxLevel = maxLevel },
            Worth = new CharmWorth { Base = 1, PerLevel = 1 },
        });
        problem.CurrentCharms[1] = new GridPos(0, 0);
        return problem;
    }

    private static List<EnchantAdvice> Rank(PlacementProblem problem) =>
        EnchantAdvisor.Rank(problem, PlacementSolver.Solve(problem));

    [Fact]
    public void AnEnchantIsWorthOneLevelOnAPlainCell()
    {
        var advice = Assert.Single(Rank(OneCell()));

        Assert.Equal(1, advice.InstanceId);
        Assert.Equal("대상", advice.Name);
        Assert.Equal(new GridPos(0, 0), advice.Position);
        Assert.Equal(1, advice.Gain, 6);
        Assert.Equal(1, advice.Enchant);
        Assert.Equal(5, advice.MaxEnchant);
    }

    /// <summary>
    /// 이 기능의 핵심이다. 인챈트는 배수보다 <b>먼저</b> 더해지므로(RESEARCH 의 "레벨이 정해지는
    /// 순서") 배수 칸에서는 +1 이 레벨 +배수가 된다. 눈으로는 세기 어려운 자리다.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void TheCellMultiplierAmplifiesTheEnchantBecauseItIsAddedFirst(int multiply, double gain)
    {
        Assert.Equal(gain, Assert.Single(Rank(OneCell(multiply: multiply))).Gain, 6);
    }

    /// <summary>
    /// 표시 레벨이 상한을 넘으면 효과는 <c>min(maxLevel, 레벨)</c>에서 잘린다. 그런 자리에 건
    /// 인챈트는 숫자만 올리고 값을 하지 않으므로 권하지 않는다.
    /// </summary>
    [Fact]
    public void AnEnchantThatOnlySpillsOverTheCapIsNotAdvised()
    {
        Assert.Empty(Rank(OneCell(cellLevel: 5, maxLevel: 5)));
    }

    /// <summary>
    /// 게임은 인챈트 수치가 <c>maxLevel</c> 에 닿으면 더 받지 않는다. 상한이 걸리는 것은 인챈트
    /// 수치 자체이지 석판이 준 레벨이 아니라서, 음수 칸에 앉아 아직 올릴 자리가 남은 아티팩트도
    /// 인챈트만 다 찼으면 거절당한다.
    /// </summary>
    [Fact]
    public void TheGameRefusesAnEnchantAtTheCapEvenWhenTheLevelStillHasRoom()
    {
        // 인챈트 4/5 는 아직 받을 수 있고, 같은 자리에서 5/5 는 받을 수 없다.
        var room = Assert.Single(Rank(OneCell(cellLevel: -3, maxLevel: 5, enchant: 4)));
        Assert.Equal(5, room.Enchant);

        Assert.Empty(Rank(OneCell(cellLevel: -3, maxLevel: 5, enchant: 5)));
    }

    [Fact]
    public void ArtifactsTheGameCannotEnchantAreNotCandidates()
    {
        // maxLevel 이 0 이면 게임이 "이 아티팩트는 인챈트할 수 없다" 로 돌려보낸다.
        Assert.Empty(Rank(OneCell(maxLevel: 0)));

        // 연동 무기를 안 든 아티팩트는 지금 효과가 없어 증가분을 잴 수가 없다.
        var dormant = OneCell();
        dormant.Charms[0].IsDormant = true;
        Assert.Empty(Rank(dormant));

        // 아티팩트가 아닌 것(소비 아이템)은 칸만 차지한다.
        var filler = OneCell();
        filler.Charms[0].IsFiller = true;
        Assert.Empty(Rank(filler));
    }

    /// <summary>
    /// F2 의 강화 우선·양보는 이득에만 곱한다. 배치에서 쓰는 잣대를 인챈트 조언에서도 그대로
    /// 쓰므로, ★ 를 건 아티팩트가 같은 값의 이웃보다 앞선다.
    /// </summary>
    [Fact]
    public void TheBuildWindowWeightOrdersTheCandidates()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 1, 2) };
        foreach (var id in new[] { 1, 2 })
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Weight = id == 2 ? 2 : 1,
                Definition = new CharmDefinition { EntityId = id, Id = "대상" + id, MaxLevel = 5 },
                Worth = new CharmWorth { Base = 1, PerLevel = 1 },
            });
            problem.CurrentCharms[id] = new GridPos(id - 1, 0);
        }

        var advice = Rank(problem);

        Assert.Equal(2, advice.Count);
        Assert.Equal(2, advice[0].InstanceId);
        Assert.Equal(2, advice[0].Gain, 6);
        Assert.Equal(1, advice[1].Gain, 6);
    }

    /// <summary>
    /// <c>OfferAdvisor.Clone</c> 은 목록만 얕게 복사한다. 슬롯을 제자리에서 고치면 기준 배치와
    /// 다음 후보가 이미 인챈트된 가방을 보게 되므로, 사본으로 갈아 끼우는 것이 맞는지 못 박는다.
    /// </summary>
    [Fact]
    public void TheTrialDoesNotEnchantTheRealInventory()
    {
        var problem = OneCell();
        var slot = problem.Charms[0];

        Rank(problem);

        Assert.Equal(0, slot.Enchant);
        Assert.Same(slot, problem.Charms[0]);
    }

    [Fact]
    public void ACancelledSearchDoesNotPublishPartialAdvice()
    {
        var problem = OneCell();
        var baseline = PlacementSolver.Solve(problem);
        Assert.Throws<OperationCanceledException>(
            () => EnchantAdvisor.Rank(problem, baseline, cancellation: new CancellationToken(true)));
    }

    /// <summary>
    /// 언제 조언을 만드는가. 제단 몫이 남았고 가까워야 하며, 멀거나 제단이 없어도 창이 열렸으면
    /// 만든다 - <b>물약은 제단 없이도 창을 열기 때문</b>이고, 이것이 없으면 물약을 마신 사람은
    /// 빈 화면을 본다. 거리를 보는 이유는 합성기와 같다: 이 조언은 공짜가 아니다.
    /// </summary>
    [Theory]
    [InlineData(null, false, null, false)]   // 제단도 창도 없다
    [InlineData(0, false, true, false)]      // 앞에 서 있지만 다 썼다
    [InlineData(1, false, false, false)]     // 몫은 남았지만 아직 멀다
    [InlineData(1, false, true, true)]       // 몫이 남았고 가깝다
    [InlineData(1, false, null, true)]       // 거리를 재지 않은 옛 자료는 전처럼 돈다
    [InlineData(0, true, false, true)]       // 물약 - 제단 몫도 없고 멀어도 창이 열렸다
    public void TheAltarOrAnOpenWindowGatesTheAdvice(int? remaining, bool open, bool? near, bool advised)
    {
        var inventory = new InventoryState { Width = 1, Height = 1, Storage = 1 };
        inventory.Items.Add(new PlacedItem { DefinitionId = 1, InstanceId = 1, Position = new GridPos(0, 0), IsActive = true });
        var snapshot = new GameSnapshot
        {
            Inventory = inventory,
            EnchantChance = remaining is null ? null
                : new EnchantChanceState { AltarUses = remaining.Value, Open = open, Near = near },
        };
        var catalog = new Catalog(
            Array.Empty<TabletDefinition>(),
            new[] { new CharmDefinition { EntityId = 1, Id = "대상", MaxLevel = 5 } });

        var plan = PlanBuilder.Build(snapshot, catalog)!;

        Assert.Equal(AdviceStatus.Ready, plan.AdviceStatus);
        Assert.Equal(advised, plan.Enchants.Count > 0);
        if (advised) Assert.Equal("대상", plan.Enchants[0].Name);
    }

    /// <summary>추천을 끈 세션은 제단이 있어도 계산하지 않는다.</summary>
    [Fact]
    public void RecommendationsOffProducesNoEnchantAdvice()
    {
        var inventory = new InventoryState { Width = 1, Height = 1, Storage = 1 };
        inventory.Items.Add(new PlacedItem { DefinitionId = 1, InstanceId = 1, Position = new GridPos(0, 0), IsActive = true });
        var snapshot = new GameSnapshot
        {
            Inventory = inventory,
            EnchantChance = new EnchantChanceState { AltarUses = 1, Near = true },
        };
        var catalog = new Catalog(
            Array.Empty<TabletDefinition>(),
            new[] { new CharmDefinition { EntityId = 1, Id = "대상", MaxLevel = 5 } });

        var plan = PlanBuilder.Build(snapshot, catalog, new PlanPreferences { Recommendations = false })!;

        Assert.Equal(AdviceStatus.NotRequested, plan.AdviceStatus);
        Assert.Empty(plan.Enchants);
    }
}
