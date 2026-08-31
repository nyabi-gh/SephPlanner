using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;

namespace SephPlanner.Tests;

/// <summary>
/// 이동 목록은 위에서부터 그대로 따라 할 수 있어야 한다. 목표 칸이 이미 차 있으면 첫 걸음부터
/// 막히는데, 목록만 보는 사람은 그걸 알 수 없다.
/// </summary>
public class MoveOrderTests
{
    private const int TabletEntity = 100;
    private const int SmallCharm = 200;
    private const int BigCharm = 201;

    /// <summary>
    /// 왼쪽 칸을 2, 오른쪽 칸을 1 올리는 석판. 상한이 1인 아티팩트는 왼쪽에 있어도 1까지만 받으니
    /// 상한이 큰 쪽에 왼쪽을 내주는 것이 이득이다. 그래서 둘은 반드시 자리를 맞바꾼다.
    /// </summary>
    private static Catalog Catalog() => new(
        new[] { new TabletDefinition { Id = "T", EntityId = TabletEntity, Query = "LEFT 2\nRIGHT 1" } },
        new[]
        {
            new CharmDefinition { Id = "A", EntityId = SmallCharm, MaxLevel = 1 },
            new CharmDefinition { Id = "B", EntityId = BigCharm, MaxLevel = 5 },
        });

    /// <summary>
    /// 두 아티팩트가 서로의 자리를 원하는 상황. 격자는 넉넉히 열어 둔다.
    /// </summary>
    private static GameSnapshot Swap(int storage)
    {
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = storage };

        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = TabletEntity,
            InstanceId = 1,
            Position = new GridPos(1, 0),
            IsApplied = true,
        });

        // 상한이 작은 쪽이 좋은 칸(왼쪽)을 차지하고 있다. 서로 바꿔야 이득이다.
        inventory.Items.Add(new PlacedItem { DefinitionId = SmallCharm, InstanceId = 10, Position = new GridPos(0, 0) });
        inventory.Items.Add(new PlacedItem { DefinitionId = BigCharm, InstanceId = 11, Position = new GridPos(2, 0) });

        return new GameSnapshot { Inventory = inventory };
    }

    [Fact]
    public void EveryStepLandsOnACellThatIsFreeByThen()
    {
        var plan = PlanBuilder.Build(Swap(storage: 6), Catalog());
        Assert.NotNull(plan);

        // 첫 배치를 그대로 재현한 뒤, 목록을 한 줄씩 실행해 본다.
        var occupied = new HashSet<GridPos>
        {
            new(1, 0), // 석판
            new(0, 0),
            new(2, 0),
        };

        foreach (var move in plan!.Moves)
        {
            Assert.True(occupied.Contains(move.From), $"{move.Label}: 출발 칸 {move.From} 이 비어 있다");
            Assert.False(occupied.Contains(move.To), $"{move.Label}: 도착 칸 {move.To} 이 이미 차 있다");

            occupied.Remove(move.From);
            occupied.Add(move.To);
        }
    }

    [Fact]
    public void ASwapIsBrokenUpWithAParkingStep()
    {
        var plan = PlanBuilder.Build(Swap(storage: 6), Catalog());

        // 맞바꾸기는 두 걸음으로 끝나지 않는다. 한쪽을 잠시 빼두는 걸음이 끼어야 한다.
        Assert.True(plan!.Moves.Count >= 3);
        Assert.Contains(plan.Moves, move => move.Detail.Contains("잠시 비켜두기"));
    }

    private const int SpinTablet = 101;

    [Fact]
    public void ARotationInPlaceIsOneStepNotAParkAndReturn()
    {
        // 자리는 그대로 두고 회전만 바꾸면 되는 석판. 자리 경쟁이 없으니 대피를 끼울 이유가 없다.
        var catalog = new Catalog(
            new[]
            {
                new TabletDefinition
                {
                    Id = "S", EntityId = SpinTablet, IsRotatable = true, Query = "RIGHT 2\nLEFT 1",
                },
            },
            new[]
            {
                new CharmDefinition { Id = "A", EntityId = SmallCharm, MaxLevel = 5 },
                new CharmDefinition { Id = "B", EntityId = BigCharm, MaxLevel = 5 },
            });

        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 18 };
        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = SpinTablet,
            InstanceId = 1,
            Position = new GridPos(1, 1),
            Rotation = 0,
            IsApplied = true,
        });

        // 위아래 이웃에 아티팩트가 이미 있어, 석판을 90도 돌리기만 하면 둘 다 레벨을 받는다.
        inventory.Items.Add(new PlacedItem { DefinitionId = SmallCharm, InstanceId = 10, Position = new GridPos(1, 0) });
        inventory.Items.Add(new PlacedItem { DefinitionId = BigCharm, InstanceId = 11, Position = new GridPos(1, 2) });

        var plan = PlanBuilder.Build(new GameSnapshot { Inventory = inventory }, catalog);

        Assert.NotNull(plan);
        var move = Assert.Single(plan!.Moves);
        Assert.Equal(move.From, move.To);
        Assert.Contains("회전", move.Detail);
    }

    [Fact]
    public void NothingIsLostWhenThereIsNowhereToPark()
    {
        // 격자가 꽉 차 대피할 칸이 없어도 옮길 것을 조용히 빠뜨리지는 않는다.
        var plan = PlanBuilder.Build(Swap(storage: 3), Catalog());

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Moves.Count);
    }
}
