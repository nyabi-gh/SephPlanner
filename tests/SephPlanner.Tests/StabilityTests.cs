using SephPlanner.Core.Charms;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 상황이 실질적으로 달라지지 않았으면 제안도 달라지면 안 된다. 이득이 없는데 물건을 옮기라고
/// 하면 사용자는 그 제안을 믿지 못하게 된다.
/// </summary>
public class StabilityTests
{
    [Fact]
    public void TwoEquallyGoodCharmsAreNotAskedToSwapPlaces()
    {
        // 석판이 양옆 두 칸에 똑같이 +1 을 준다. 두 아티팩트가 이미 그 두 칸에 있으니
        // 서로 자리를 바꿔도 점수는 같다. 그래도 옮기라고 해서는 안 된다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 1,
            Definition = new TabletDefinition { Id = "T", Query = "LEFT 1\nRIGHT 1" },
        });
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });
        problem.Charms.Add(new CharmSlot { InstanceId = 11, Definition = new CharmDefinition { MaxLevel = 5 } });

        // 배정기가 자연히 고르는 순서와 반대로 놓여 있어야 실제로 맞바꾸기가 생긴다.
        problem.CurrentTablets[1] = new TabletSpot(new GridPos(1, 0), 0);
        problem.CurrentCharms[10] = new GridPos(2, 0);
        problem.CurrentCharms[11] = new GridPos(0, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(2, 0), arrangement.CharmPositions[10]);
        Assert.Equal(new GridPos(0, 0), arrangement.CharmPositions[11]);
    }

    [Fact]
    public void FillersAreNotAskedToSwapPlaces()
    {
        // 필러는 어느 칸이든 0점 동률이다. 그래도 필러끼리 자리를 맞바꾸라고 해서는 안 된다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Charms.Add(new CharmSlot { InstanceId = 10, IsFiller = true });
        problem.Charms.Add(new CharmSlot { InstanceId = 11, IsFiller = true });

        // 배정기가 자연히 고르는 순서와 반대로 놓여 있어야 실제로 맞바꾸기가 생긴다.
        problem.CurrentCharms[10] = new GridPos(1, 0);
        problem.CurrentCharms[11] = new GridPos(0, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(1, 0), arrangement.CharmPositions[10]);
        Assert.Equal(new GridPos(0, 0), arrangement.CharmPositions[11]);
    }

    private static Catalog ReorderableCatalog() => new(
        new[]
        {
            new TabletDefinition { Id = "T1", EntityId = 100, Query = "RIGHT 2" },
            new TabletDefinition { Id = "T2", EntityId = 101, Query = "LEFT 1" },
        },
        new[]
        {
            new CharmDefinition { Id = "C1", EntityId = 200, MaxLevel = 5 },
            new CharmDefinition { Id = "C2", EntityId = 201, MaxLevel = 5 },
            new CharmDefinition { Id = "C3", EntityId = 202, MaxLevel = 5 },
        });

    private static GameSnapshot ReorderableSnapshot(bool reversed)
    {
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 12 };
        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = 100, InstanceId = 1, Position = new GridPos(0, 0), IsApplied = true,
        });
        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = 101, InstanceId = 2, Position = new GridPos(3, 0), IsApplied = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 200, InstanceId = 10, Position = new GridPos(1, 0), IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 201, InstanceId = 11, Position = new GridPos(2, 0), IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 202, InstanceId = 12, Position = new GridPos(4, 0), IsActive = true,
        });

        var snapshot = new GameSnapshot
        {
            Inventory = inventory,
            Run = new RunState { Gold = 1000 },
        };
        snapshot.Offers.Add(new OfferedItem { DefinitionId = 201, Kind = "charm" });
        snapshot.Offers.Add(new OfferedItem { DefinitionId = 202, Kind = "charm" });

        if (reversed)
        {
            inventory.Tablets.Reverse();
            inventory.Items.Reverse();
            snapshot.Offers.Reverse();
        }
        return snapshot;
    }

    private static void AssertSamePlan(Plan left, Plan right)
    {
        Assert.Equal(left.Best.Score, right.Best.Score, 9);

        Assert.Equal(left.Best.CharmPositions.Count, right.Best.CharmPositions.Count);
        foreach (var pair in left.Best.CharmPositions)
            Assert.Equal(pair.Value, right.Best.CharmPositions[pair.Key]);

        Assert.Equal(
            left.Best.Tablets.Select(t => (t.Definition.EntityId, t.Position, t.Rotation)),
            right.Best.Tablets.Select(t => (t.Definition.EntityId, t.Position, t.Rotation)));

        Assert.Equal(
            left.Moves.Select(m => m.Label + "|" + m.Detail),
            right.Moves.Select(m => m.Label + "|" + m.Detail));

        Assert.Equal(
            left.Offers.Select(o => o.Candidate.DefinitionId),
            right.Offers.Select(o => o.Candidate.DefinitionId));
    }

    [Fact]
    public void TheSamePlanComesOutWhateverOrderTheSnapshotListsThingsIn()
    {
        // 게임이 넘겨주는 목록 순서는 물건을 옮기면 바뀐다. 점수만이 아니라 배치와 이동,
        // 후보 순서까지 같아야 제안이 흔들리지 않는다.
        var forward = PlanBuilder.Build(ReorderableSnapshot(reversed: false), ReorderableCatalog());
        var backward = PlanBuilder.Build(ReorderableSnapshot(reversed: true), ReorderableCatalog());

        AssertSamePlan(forward!, backward!);
    }

    [Fact]
    public void BuildingTwiceFromTheSameSnapshotGivesTheSamePlan()
    {
        var first = PlanBuilder.Build(ReorderableSnapshot(reversed: false), ReorderableCatalog());
        var second = PlanBuilder.Build(ReorderableSnapshot(reversed: false), ReorderableCatalog());

        AssertSamePlan(first!, second!);
    }

    [Fact]
    public void ConditionalCharmsNeverProduceALosingProposal()
    {
        // 조건 판정과 배정이 서로 물려 수렴 반복이 소진될 수 있다. 그래도 지금 배치보다
        // 나쁜 배치로 옮기라고 하면 안 된다 - 이득이 없으면 이동도 없어야 한다.
        var catalog = new Catalog(
            new[] { new TabletDefinition { Id = "T", EntityId = 100, Query = "RIGHT 1" } },
            new[]
            {
                new CharmDefinition
                {
                    Id = "S1", EntityId = 200, MaxLevel = 5, CriteriaType = "BothSidesAreEmpty",
                },
                new CharmDefinition
                {
                    Id = "S2", EntityId = 201, MaxLevel = 5, CriteriaType = "BothSidesAreEmpty",
                },
                new CharmDefinition
                {
                    Id = "N1", EntityId = 202, MaxLevel = 5, CriteriaType = "NeighborsAreFull",
                },
            });

        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 6 };
        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = 100, InstanceId = 1, Position = new GridPos(5, 0), IsApplied = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 200, InstanceId = 10, Position = new GridPos(1, 0), IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 201, InstanceId = 11, Position = new GridPos(3, 0), IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 202, InstanceId = 12, Position = new GridPos(0, 0), IsActive = true,
        });

        var plan = PlanBuilder.Build(
            new GameSnapshot { Inventory = inventory, Run = new RunState() }, catalog);

        Assert.True(plan!.Gain >= 0, $"Gain {plan.Gain} 은 음수면 안 된다");
        Assert.True(plan.Moves.Count == 0 || plan.Gain > 1e-9,
            $"이득 {plan.Gain} 없이 이동 {plan.Moves.Count}건을 제안했다");
    }

    /// <summary>계획의 목표 배치. 인스턴스마다 어느 칸(과 회전)으로 가라는지다.</summary>
    private static Dictionary<int, (GridPos Position, int Rotation)> TargetsOf(Plan plan) =>
        plan.Targets.ToDictionary(t => t.InstanceId, t => (t.To, t.Rotation));

    /// <summary>스냅샷에서 from 칸의 것을 to 로 옮긴다. to 가 차 있으면 게임처럼 맞바꾼다.</summary>
    private static void ApplyMove(InventoryState inventory, GridPos from, GridPos to, int rotation)
    {
        var tablet = inventory.Tablets.FirstOrDefault(t => t.Position == from);
        var item = inventory.Items.FirstOrDefault(i => i.Position == from);
        var tabletAtTo = inventory.Tablets.FirstOrDefault(t => t.Position == to);
        var itemAtTo = inventory.Items.FirstOrDefault(i => i.Position == to);

        if (tabletAtTo != null) tabletAtTo.Position = from;
        if (itemAtTo != null) itemAtTo.Position = from;
        if (tablet != null)
        {
            tablet.Position = to;
            tablet.Rotation = rotation;
        }
        if (item != null) item.Position = to;
    }

    [Fact]
    public void FollowingThePlanOneMoveAtATimeDoesNotChangeThePlan()
    {
        // 사용자가 제안을 한 수씩 손으로 따라가는 동안 목표 배치가 계속 바뀌면 제안을 따라갈
        // 수가 없다. 서로 바꿔 놓아도 점수가 같은 아티팩트(같은 종류 여럿)와 대칭 석판이
        // 동점 배치를 많이 만들어야 실제로 흔들린다.
        var catalog = new Catalog(
            new[]
            {
                new TabletDefinition { Id = "T1", EntityId = 100, Query = "HORIZONTAL 2" },
                new TabletDefinition { Id = "T2", EntityId = 101, Query = "RIGHT 2" },
            },
            new[] { new CharmDefinition { Id = "C", EntityId = 200, MaxLevel = 5 } });

        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 12 };
        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = 100, InstanceId = 1, Position = new GridPos(5, 1), IsApplied = true,
        });
        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = 101, InstanceId = 2, Position = new GridPos(0, 0), IsApplied = true,
        });
        for (var i = 0; i < 4; i++)
        {
            inventory.Items.Add(new PlacedItem
            {
                DefinitionId = 200, InstanceId = 10 + i,
                Position = new GridPos(i, 1), IsActive = true,
            });
        }

        var snapshot = new GameSnapshot { Inventory = inventory, Run = new RunState() };
        var plan = PlanBuilder.Build(snapshot, catalog)!;
        var promised = TargetsOf(plan);

        // 계획이 끝날 때까지 첫 수를 하나씩 따라간다. 걸음 수 상한은 무한 흔들림 탐지용이다.
        for (var step = 0; step < 20 && plan.Moves.Count > 0; step++)
        {
            var move = plan.Moves[0];
            var rotation = plan.Targets
                .Where(t => t.IsTablet && t.To == move.To)
                .Select(t => t.Rotation)
                .FirstOrDefault();
            ApplyMove(inventory, move.From, move.To, rotation);

            plan = PlanBuilder.Build(snapshot, catalog)!;
            var next = TargetsOf(plan);

            Assert.Equal(promised.Count, next.Count);
            foreach (var pair in promised)
            {
                Assert.True(next.TryGetValue(pair.Key, out var target)
                            && target == pair.Value,
                    $"걸음 {step} 뒤 인스턴스 {pair.Key} 의 목표가 {pair.Value} 에서 " +
                    $"{(next.TryGetValue(pair.Key, out var moved) ? moved.ToString() : "없음")} 로 바뀌었다");
            }
        }

        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void ThePreviousProposalIsKeptAmongEqualTargets()
    {
        // 같은 종류 아티팩트 둘에게 똑같이 좋은 칸이 둘이면 어느 배정이든 점수가 같다.
        // 직전 제안이 있으면 그쪽을 따라야, 계획을 따라가는 동안 목표가 저희끼리 뒤바뀌지 않는다.
        var catalog = new Catalog(
            new[] { new TabletDefinition { Id = "T", EntityId = 100, Query = "LEFT 1\nRIGHT 1" } },
            new[] { new CharmDefinition { Id = "C", EntityId = 200, MaxLevel = 5 } });

        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 6 };
        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = 100, InstanceId = 1, Position = new GridPos(1, 0), IsApplied = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 200, InstanceId = 10, Position = new GridPos(4, 0), IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = 200, InstanceId = 11, Position = new GridPos(5, 0), IsActive = true,
        });
        var snapshot = new GameSnapshot { Inventory = inventory, Run = new RunState() };

        // 배정기가 자연히 고르는 것과 반대 방향의 직전 제안.
        var previous = new Plan();
        previous.Targets.Add(new PlanTarget { InstanceId = 1, To = new GridPos(1, 0), IsTablet = true });
        previous.Targets.Add(new PlanTarget { InstanceId = 10, To = new GridPos(2, 0) });
        previous.Targets.Add(new PlanTarget { InstanceId = 11, To = new GridPos(0, 0) });

        var plan = PlanBuilder.Build(snapshot, catalog, null, previous)!;

        Assert.Equal(new GridPos(2, 0), plan.Best.CharmPositions[10]);
        Assert.Equal(new GridPos(0, 0), plan.Best.CharmPositions[11]);
    }

    [Fact]
    public void ASymmetricTabletKeepsItsRotationWhenMoved()
    {
        // "LEFT 1\nRIGHT 1" 은 180도 돌려도 효과가 같다. 옮기면서 회전까지 0으로 되돌리라고
        // 하면 아무 효과 없는 손동작을 시키는 셈이다 - 실제 세션에서 쌍성이 그런 지시를 받았다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 1,
            Definition = new TabletDefinition { Id = "T", Query = "LEFT 1\nRIGHT 1", IsRotatable = true },
            Rotatable = true,
        });
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });
        problem.Charms.Add(new CharmSlot { InstanceId = 11, Definition = new CharmDefinition { MaxLevel = 5 } });

        // 구석에 있어 옮겨야 하고, 지금 180도 돌아가 있다.
        problem.CurrentTablets[1] = new TabletSpot(new GridPos(5, 0), 2);
        problem.CurrentCharms[10] = new GridPos(3, 0);
        problem.CurrentCharms[11] = new GridPos(4, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.NotEqual(new GridPos(5, 0), arrangement.Tablets[0].Position);
        Assert.Equal(2, arrangement.Tablets[0].Rotation);
    }

    [Fact]
    public void AGenuineImprovementIsStillProposed()
    {
        // 안정성 때문에 진짜 이득까지 놓치면 안 된다. 지금은 아무도 좋은 칸에 있지 않다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 1,
            Definition = new TabletDefinition { Id = "T", Query = "RIGHT 3" },
        });
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });

        problem.CurrentTablets[1] = new TabletSpot(new GridPos(0, 0), 0);
        problem.CurrentCharms[10] = new GridPos(4, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(1, 0), arrangement.CharmPositions[10]);
        Assert.Equal(4, arrangement.Score, 2);
    }
}
