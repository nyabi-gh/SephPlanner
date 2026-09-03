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
    /// 레벨 하나가 STRONG 에게 5, WEAK 에게 1 이다. 좋은 칸(+2)을 STRONG 이 차지한 상태에서
    /// 시작하므로, WEAK 를 그 칸으로 옮기려면 지정 배수가 그 차이를 넘어야 한다.
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

    private static GameSnapshot Snapshot()
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
            Position = new GridPos(2, 0),
            IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = WeakEntity,
            InstanceId = 11,
            Position = new GridPos(1, 0),
            IsActive = true,
        });
        return new GameSnapshot { Inventory = inventory, Run = new RunState() };
    }

    /// <summary>
    /// 둘이 실제로 받는 레벨. 칸 번호로 단정하지 않는 것은 솔버가 석판도 함께 옮기기 때문이다 -
    /// 어느 칸이 좋은 칸이 되는지는 답의 일부이지 전제가 아니다. 석판이 하나뿐이라 올려 받는
    /// 쪽은 언제나 하나이므로, 둘 중 누가 받았는지가 곧 이 지정이 이겼는지다.
    /// </summary>
    private static (int Weak, int Strong) LevelsWithPin(int pinLevel)
    {
        var preferences = new PlanPreferences { CharmValues = Values() };
        if (pinLevel > 0) preferences.PinnedCharms[WeakEntity] = pinLevel;

        var plan = PlanBuilder.Build(Snapshot(), Catalog(), preferences)!;
        return (LevelOf(plan, 11), LevelOf(plan, 10));
    }

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
        for (var level = 1; level <= PlanPreferences.MaxPinLevel; level++)
            Assert.True(PlanPreferences.WeightOf(level) > PlanPreferences.WeightOf(level - 1));
    }
}
