using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 강화 우선 지정은 "이 아티팩트를 좋은 칸에 두라"는 사용자의 말이다. 단계마다 배수가 다르고,
/// 그 배수는 값어치가 더 큰 아티팩트를 어디까지 밀어낼 수 있는지로 뜻이 정해진다.
/// </summary>
public class PinnedWeightTests
{
    private const int TabletEntity = 100;
    private const int StrongEntity = 200;
    private const int WeakEntity = 201;

    /// <summary>오른쪽 두 칸째에 +2 를 주는 석판 하나와, 값어치가 크게 다른 아티팩트 둘.</summary>
    private static Catalog Catalog() => new(
        new[] { new TabletDefinition { Id = "T", EntityId = TabletEntity, Query = "RIGHT 2" } },
        new[]
        {
            new CharmDefinition { Id = "STRONG", EntityId = StrongEntity, MaxLevel = 5 },
            new CharmDefinition { Id = "WEAK", EntityId = WeakEntity, MaxLevel = 5 },
        });

    /// <summary>
    /// 레벨 하나가 STRONG 에게 5, WEAK 에게 1 이다. 석판이 (1,0) 에 +2 를 주고, 기본 판에서는
    /// WEAK 가 그 칸에 있어 STRONG 을 데려오려면 맞바꿈(이사 비용 0.6)을 치러야 한다. 양보를
    /// 재는 판은 반대로 STRONG 이 그 칸에 있어, 내려간 STRONG 을 내보내려면 배수가 값어치
    /// 차이를 뒤집어야 한다.
    /// </summary>
    private static CharmValueBook Values() => new(new CharmValueFile
    {
        Version = 1,
        Charms =
        {
            new CharmValueEntry { Id = "STRONG", EntityId = StrongEntity, Base = 1.5, PerLevel = 5 },
            new CharmValueEntry { Id = "WEAK", EntityId = WeakEntity, Base = 1.5, PerLevel = 1 },
        },
    });

    private static GameSnapshot Snapshot(bool strongOnGoodCell = false)
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
            DefinitionId = StrongEntity,
            InstanceId = 10,
            Position = strongOnGoodCell ? new GridPos(1, 0) : new GridPos(2, 0),
            IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = WeakEntity,
            InstanceId = 11,
            Position = strongOnGoodCell ? new GridPos(2, 0) : new GridPos(1, 0),
            IsActive = true,
        });
        return new GameSnapshot { Inventory = inventory, Run = new RunState() };
    }

    /// <summary>
    /// 둘이 실제로 받는 레벨. 칸 번호로 단정하지 않는 것은 솔버가 석판도 함께 옮기기 때문이다 -
    /// 어느 칸이 좋은 칸이 되는지는 답의 일부이지 전제가 아니다. 석판이 하나뿐이라 올려 받는
    /// 쪽은 언제나 하나이므로, 둘 중 누가 받았는지가 곧 이 지정이 이겼는지다.
    /// </summary>
    private static (int Weak, int Strong) LevelsWithPin(int pinLevel) =>
        LevelsWithPins(weakPin: pinLevel, strongPin: 0);

    private static (int Weak, int Strong) LevelsWithPins(int weakPin, int strongPin, bool strongOnGoodCell = false) =>
        LevelsOf(PlanWithPins(weakPin, strongPin, strongOnGoodCell));

    private static Plan PlanWithPins(int weakPin, int strongPin, bool strongOnGoodCell)
    {
        var preferences = new PlanPreferences { CharmValues = Values() };
        if (weakPin != 0) preferences.PinnedCharms[WeakEntity] = weakPin;
        if (strongPin != 0) preferences.PinnedCharms[StrongEntity] = strongPin;

        return PlanBuilder.Build(Snapshot(strongOnGoodCell), Catalog(), preferences)!;
    }

    private static (int Weak, int Strong) LevelsOf(Plan plan) => (LevelOf(plan, 11), LevelOf(plan, 10));

    private static int LevelOf(Plan plan, int instanceId)
    {
        var cell = plan.Best.CharmPositions[instanceId];
        return plan.Best.EffectiveLevels.TryGetValue(cell, out var level) ? level : 0;
    }

    /// <summary>지정하지 않으면 값어치대로 STRONG 이 올려 받는다.</summary>
    [Fact]
    public void WithNoPinTheMoreValuableCharmIsTheOneRaised()
    {
        var (weak, strong) = LevelsWithPin(pinLevel: 0);

        Assert.True(strong > weak, $"STRONG {strong} 이 WEAK {weak} 보다 높아야 한다");
    }

    /// <summary>
    /// 낮은 단계는 값어치 차이를 못 넘는다. 넘어 버리면 살짝 찍은 지정 하나가 판을 뒤집게 되어
    /// 단계를 둔 뜻이 없어진다.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ALightPinDoesNotOutweighAMuchMoreValuableCharm(int level)
    {
        var (weak, strong) = LevelsWithPin(level);

        Assert.True(strong > weak, $"{level}단계에서 WEAK {weak} 이 STRONG {strong} 을 이겼다");
    }

    /// <summary>가장 무거운 단계는 값어치가 더 큰 것을 밀어내고 올려 받는다.</summary>
    [Fact]
    public void TheHeaviestPinIsRaisedEvenAtTheOtherCharmsExpense()
    {
        var (weak, strong) = LevelsWithPin(PlanPreferences.MaxPinLevel);

        Assert.True(weak > strong, $"최고 단계인데 WEAK {weak} 이 STRONG {strong} 을 못 이겼다");
    }

    /// <summary>단계가 올라가면 배수도 올라간다. 순서가 뒤집히면 ★ 개수가 거짓말이 된다.</summary>
    [Fact]
    public void EachStepWeighsMoreThanTheOneBelow()
    {
        Assert.Equal(1.0, PlanPreferences.WeightOf(0));
        for (var level = PlanPreferences.MinPinLevel + 1; level <= PlanPreferences.MaxPinLevel; level++)
            Assert.True(PlanPreferences.WeightOf(level) > PlanPreferences.WeightOf(level - 1));
    }

    /// <summary>
    /// 내리는 단계는 1 보다 작되 0 보다 크다. 음수면 솔버가 그 아티팩트를 꺼지는 칸으로 보내려
    /// 하고, 막상 거기 가면 꺼진 것으로 잡혀 의도가 흐지부지된다.
    /// </summary>
    [Fact]
    public void ADownwardStepIsAFractionNotANegative()
    {
        for (var level = -1; level >= PlanPreferences.MinPinLevel; level--)
        {
            var weight = PlanPreferences.WeightOf(level);
            Assert.True(weight > 0 && weight < 1, $"{level}단계의 배수 {weight}");
        }
    }

    /// <summary>
    /// 살짝 양보한 STRONG 은 여전히 WEAK 보다 값어치가 커 좋은 칸을 지킨다. 넘어 버리면 살짝
    /// 내린 지정 하나가 판을 뒤집게 되어 단계를 둔 뜻이 없어진다.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    public void ALightYieldDoesNotGiveUpToAMuchLessValuableCharm(int level)
    {
        var (weak, strong) = LevelsWithPins(weakPin: 0, strongPin: level, strongOnGoodCell: true);

        Assert.True(strong > weak, $"{level}단계에서 STRONG {strong} 이 WEAK {weak} 에게 자리를 내줬다");
    }

    /// <summary>가장 낮은 단계는 값어치가 훨씬 작은 것에게도 좋은 칸을 내준다.</summary>
    [Fact]
    public void TheDeepestYieldGivesTheGoodCellAway()
    {
        var (weak, strong) = LevelsWithPins(weakPin: 0, strongPin: PlanPreferences.MinPinLevel, strongOnGoodCell: true);

        Assert.True(weak > strong, $"최저 단계인데 STRONG {strong} 이 WEAK {weak} 에게 자리를 안 내줬다");
    }

    /// <summary>양보한 아티팩트도 꺼지지는 않는다. 배수가 0 보다 크므로 켜져 있는 칸이 남으면 거기 간다.</summary>
    [Fact]
    public void AYieldedCharmStillPrefersACellWhereItWorks()
    {
        var plan = PlanWithPins(weakPin: 0, strongPin: PlanPreferences.MinPinLevel, strongOnGoodCell: true);

        Assert.DoesNotContain(10, plan.Best.InactiveCharms);
    }
}
