using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 이동 목록은 위에서부터 그대로 따라 할 수 있어야 한다. 목표 칸이 이미 차 있으면 첫 걸음부터
/// 막히는데, 목록만 보는 사람은 그걸 알 수 없다.
/// </summary>
public class MoveOrderTests
{
    [Fact]
    public void EverySixItemPermutationCanBeFollowedWithoutAnEmptyCell()
    {
        var target = Enumerable.Range(0, 6).ToArray();
        CheckPermutations(0);

        void CheckPermutations(int start)
        {
            if (start < target.Length)
            {
                for (var index = start; index < target.Length; index++)
                {
                    (target[start], target[index]) = (target[index], target[start]);
                    CheckPermutations(start + 1);
                    (target[start], target[index]) = (target[index], target[start]);
                }
                return;
            }

            var pending = Enumerable.Range(0, 6).Select(index => new Relocation
            {
                Name = "같은 이름",
                From = new GridPos(index, 0),
                To = new GridPos(target[index], 0),
                FromRotation = 0,
                ToRotation = index % 4,
            }).ToList();
            var moves = MoveOrder.Sequence(new GridSpec(6, 1, 6), pending, Array.Empty<GridPos>(), out var complete);
            Assert.True(complete);
            var occupants = Enumerable.Range(0, 6).ToArray();
            var turns = new HashSet<int>();
            foreach (var move in moves)
            {
                if (move.From == move.To)
                {
                    var instance = occupants[move.From.X];
                    Assert.Equal(target[instance], move.From.X);
                    Assert.NotEqual(0, instance % 4);
                    Assert.True(turns.Add(instance));
                }
                else
                    (occupants[move.From.X], occupants[move.To.X]) = (occupants[move.To.X], occupants[move.From.X]);
            }
            for (var index = 0; index < target.Length; index++) Assert.Equal(index, occupants[target[index]]);
            Assert.Equal(4, turns.Count);
            Assert.True(moves.Count(move => move.From != move.To) <= 5);
        }
    }

    [Fact]
    public void DuplicateOrBlockedTargetsDoNotProducePartialInstructions()
    {
        var pending = new List<Relocation>
        {
            new() { From = new GridPos(0, 0), To = new GridPos(1, 0) },
            new() { From = new GridPos(1, 0), To = new GridPos(1, 0) },
        };
        Assert.Empty(MoveOrder.Sequence(new GridSpec(3, 1, 3), pending, Array.Empty<GridPos>(), out var complete));
        Assert.False(complete);
        pending.RemoveAt(1);
        Assert.Empty(MoveOrder.Sequence(new GridSpec(3, 1, 3), pending, new[] { new GridPos(1, 0) }, out complete));
        Assert.False(complete);
    }
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
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = SmallCharm,
            InstanceId = 10,
            Position = new GridPos(0, 0),
            EffectiveLevel = 2,
            IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = BigCharm,
            InstanceId = 11,
            Position = new GridPos(2, 0),
            EffectiveLevel = 1,
            IsActive = true,
        });
        inventory.LevelMatrix["0,0"] = 2;
        inventory.LevelMatrix["2,0"] = 1;

        return new GameSnapshot { Inventory = inventory };
    }

    [Fact]
    public void FollowingSwapsReachesTheTargetInstances()
    {
        var plan = PlanBuilder.Build(Swap(storage: 6), Catalog());
        Assert.NotNull(plan);

        // 첫 배치를 그대로 재현한 뒤, 목록을 한 줄씩 실행해 본다.
        var occupied = new Dictionary<GridPos, int>
        {
            [new(1, 0)] = 1,
            [new(0, 0)] = 10,
            [new(2, 0)] = 11,
        };

        foreach (var move in plan!.Moves)
        {
            Assert.True(occupied.TryGetValue(move.From, out var moving));
            occupied.TryGetValue(move.To, out var displaced);
            occupied.Remove(move.From);
            occupied[move.To] = moving;
            if (displaced != 0) occupied[move.From] = displaced;
        }
        foreach (var target in plan.Targets) Assert.Equal(target.InstanceId, occupied[target.To]);
    }

    [Fact]
    public void ASwapNeedsOnlyOneInstruction()
    {
        var plan = PlanBuilder.Build(Swap(storage: 6), Catalog());

        Assert.Contains("교환", Assert.Single(plan!.Moves).Detail);
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
    public void AFullBagStillSupportsManualSwaps()
    {
        var plan = PlanBuilder.Build(Swap(storage: 3), Catalog());

        Assert.NotNull(plan);
        Assert.True(plan!.ManualMoveInstructionsAvailable);
        Assert.True(plan.HasPlacementChanges);
        Assert.Contains("교환", Assert.Single(plan.Moves).Detail);
        Assert.NotEmpty(plan.Targets);
    }
}
