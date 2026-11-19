using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 게임 1.1.2 의 합치기형 배치 보너스(날개 + 천 갑옷 → 사과주스)는 재료가 상하좌우로 이어지면 둘 다 지운다. 배치
/// 추천이 그렇게 붙이면 되돌릴 수 없다.
/// </summary>
public class CombineRecipeTests
{
    private const int Tablet = 100, Wing = 1143, Cloth = 1215;

    private static Catalog Catalog() => new(
        new[] { new TabletDefinition { Id = "T", EntityId = Tablet, Query = "RIGHT 2" } },
        new[]
        {
            new CharmDefinition { Id = "Wing", EntityId = Wing, MaxLevel = 5 },
            new CharmDefinition { Id = "Cloth", EntityId = Cloth, MaxLevel = 5 },
        });

    private static GameSnapshot Snapshot(List<List<int>>? recipes, GridPos cloth)
    {
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 12 };
        inventory.Tablets.Add(new PlacedTablet { DefinitionId = Tablet, InstanceId = 1, Position = new GridPos(0, 0), IsApplied = true });
        inventory.Items.Add(new PlacedItem { DefinitionId = Wing, InstanceId = 10, Position = new GridPos(1, 0), EffectiveLevel = 2, IsActive = true });
        inventory.Items.Add(new PlacedItem { DefinitionId = Cloth, InstanceId = 11, Position = cloth, EffectiveLevel = 0, IsActive = true });
        inventory.LevelMatrix["1,0"] = 2;
        return new GameSnapshot { Inventory = inventory, Run = new RunState { CombineRecipes = recipes } };
    }

    [Fact]
    public void TheSuggestedPlacementKeepsTheMaterialsApart()
    {
        var plan = PlanBuilder.Build(Snapshot(new List<List<int>> { new() { Wing, Cloth } }, new GridPos(2, 0)), Catalog())!;

        Assert.NotEmpty(plan.Current.CombinedCharms);
        Assert.Empty(plan.Best.CombinedCharms);
        var wing = plan.Best.CharmPositions[10];
        var cloth = plan.Best.CharmPositions[11];
        Assert.NotEqual(1, Math.Abs(wing.X - cloth.X) + Math.Abs(wing.Y - cloth.Y));
        Assert.Single(plan.RetentionWarnings);
    }

    /// <summary>
    /// 떼어 둘 자리가 없어 추천도 붙은 채면 적용하지 않고, 사유는 끄기 허용이 아니라 합쳐짐으로 댄다.
    /// </summary>
    [Fact]
    public void MaterialsThatCannotBeKeptApartBlockTheSuggestionForThatReason()
    {
        var inventory = new InventoryState { Width = 2, Height = 1, Storage = 2 };
        inventory.Items.Add(new PlacedItem { DefinitionId = Wing, InstanceId = 10, Position = new GridPos(0, 0), IsActive = true });
        inventory.Items.Add(new PlacedItem { DefinitionId = Cloth, InstanceId = 11, Position = new GridPos(1, 0), IsActive = true });
        var snapshot = new GameSnapshot
        {
            Inventory = inventory,
            Run = new RunState { CombineRecipes = new List<List<int>> { new() { Wing, Cloth } } },
        };

        var plan = PlanBuilder.Build(snapshot, Catalog())!;

        Assert.NotEmpty(plan.Best.CombinedCharms);
        Assert.False(plan.HasUnapprovedDeactivation);
        Assert.Empty(plan.Targets);
        Assert.Contains(plan.RetentionWarnings, warning => warning.Contains("떼어 둘 자리를 찾지 못해"));
        Assert.DoesNotContain(plan.RetentionWarnings, warning => warning.Contains("추천 배치는 떼어 둡니다"));
    }

    [Fact]
    public void DiagonalMaterialsDoNotCombine()
    {
        var plan = PlanBuilder.Build(Snapshot(new List<List<int>> { new() { Wing, Cloth } }, new GridPos(2, 1)), Catalog())!;

        Assert.Empty(plan.Current.CombinedCharms);
    }

    /// <summary>능력치가 없으면 게임이 레시피를 찾지 않으므로 붙여도 된다.</summary>
    [Fact]
    public void WithoutTheStatNothingCombines()
    {
        var plan = PlanBuilder.Build(Snapshot(null, new GridPos(2, 0)), Catalog())!;

        Assert.Empty(plan.Current.CombinedCharms);
        Assert.Empty(plan.RetentionWarnings);
    }

    [Fact]
    public void TheRecipesEnterTheFingerprint()
    {
        var at = new GridPos(3, 0);
        var plain = PlanFingerprint.Placement(Snapshot(null, at), "");
        Assert.Equal(plain, PlanFingerprint.Placement(Snapshot(new List<List<int>>(), at), ""));
        Assert.NotEqual(plain, PlanFingerprint.Placement(Snapshot(new List<List<int>> { new() { Wing, Cloth } }, at), ""));
    }

    /// <summary>후보 조언은 판을 복제해 푼다. 레시피를 흘리면 후보를 놓는 자리가 재료를 붙인다.</summary>
    [Fact]
    public void AnOfferTrialKeepsTheRecipes()
    {
        var problem = new PlacementProblem { CombineRecipes = { new List<int> { Wing, Cloth } } };

        Assert.Same(problem.CombineRecipes, OfferAdvisor.Clone(problem).CombineRecipes);
    }

    // ---- 옮기는 도중: 끝 배치만 떼어 두어서는 모자라다 ----

    private static bool Adjacent(GridPos a, GridPos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

    /// <summary>1번이 날개, 2번이 천 갑옷이고 나머지는 재료가 아닌 판.</summary>
    private static PlacementProblem Board(GridSpec grid, IReadOnlyList<GridPos> cells)
    {
        var problem = new PlacementProblem { Grid = grid, CombineRecipes = { new List<int> { Wing, Cloth } } };
        for (var index = 0; index < cells.Count; index++)
        {
            var id = index + 1;
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Definition = new CharmDefinition { EntityId = id == 1 ? Wing : id == 2 ? Cloth : 2000 + id, MaxLevel = 5 },
            });
            problem.CurrentCharms[id] = cells[index];
        }
        return problem;
    }

    /// <summary>순서대로 하나씩 제자리에 넣으며(게임의 칸 교환) 재료가 맞붙는 중간 배치가 있는지 본다.</summary>
    private static bool TouchesInTransit(
        IReadOnlyDictionary<int, GridPos> start, IEnumerable<(int Id, GridPos To)> steps, out Dictionary<int, GridPos> end)
    {
        end = new Dictionary<int, GridPos>(start);
        var touched = false;
        foreach (var (id, to) in steps)
        {
            var from = end[id];
            foreach (var other in end.Where(pair => pair.Value == to && pair.Key != id).Select(pair => pair.Key).ToList())
                end[other] = from;
            end[id] = to;
            touched |= Adjacent(end[1], end[2]);
        }
        return touched;
    }

    [Fact]
    public void TheMoveOrderNeverPutsTheMaterialsTogetherOnTheWay()
    {
        var grid = new GridSpec(6, 3, 18);
        var cells = Enumerable.Range(0, 18).Select(grid.ToPosition).ToList();
        int reordered = 0, kept = 0;
        var missing = new List<int>();
        for (var seed = 0; seed < 3000; seed++)
        {
            var random = new Random(seed);
            var from = cells.OrderBy(_ => random.Next()).Take(10).ToList();
            var to = cells.OrderBy(_ => random.Next()).Take(10).ToList();
            if (Adjacent(from[0], from[1]) || Adjacent(to[0], to[1])) continue;

            var problem = Board(grid, from);
            var movers = Enumerable.Range(0, 10).Select(index => (Id: index + 1, To: to[index])).ToList();
            var unsafeAsGiven = TouchesInTransit(problem.CurrentCharms, movers, out _);

            var order = CombineRecipes.SafeOrder(problem, problem.CurrentCharms, movers);

            if (order is null) { missing.Add(seed); continue; }
            Assert.Equal(movers.Select(mover => mover.Id).OrderBy(id => id), order!.OrderBy(id => id));
            var steps = order.Select(id => movers.Single(mover => mover.Id == id)).ToList();
            Assert.False(TouchesInTransit(problem.CurrentCharms, steps, out var end), $"판 {seed}: 도중에 재료가 맞붙는다");
            foreach (var mover in movers) Assert.Equal(mover.To, end[mover.Id]);

            // 받은 순서가 안전하면 손대지 않는다 - 재료가 없는 판의 이동 순서가 달라지면 안 된다.
            if (unsafeAsGiven) reordered++;
            else
            {
                kept++;
                Assert.Equal(movers.Select(mover => mover.Id), order);
            }
        }
        // 한 걸음이 늘 제자리로 넣는 교환이라, 재료를 잠깐 비켜 두어야만 풀리는 판은 못 찾는다. 그런 판은 적용을 막는다.
        Assert.True(reordered > 100 && kept > 100 && missing.Count < reordered / 5,
            $"고친 판 {reordered}, 그대로 둔 판 {kept}, 못 찾은 판 {missing.Count}");
    }

    /// <summary>
    /// 꽉 찬 2x2 에서 대각선의 두 재료를 다른 대각선으로 옮기는 길은 어느 교환으로 시작하든 둘이 맞붙는다.
    /// 그때는 순서가 없다고 답하고 계획이 적용을 막는다.
    /// </summary>
    [Fact]
    public void ABoardWithNoSafeOrderSaysSo()
    {
        var problem = Board(new GridSpec(2, 2, 4), new[] { new GridPos(0, 0), new GridPos(1, 1), new GridPos(1, 0), new GridPos(0, 1) });
        var movers = new List<(int Id, GridPos To)>
        {
            (1, new GridPos(1, 0)), (2, new GridPos(0, 1)), (3, new GridPos(0, 0)), (4, new GridPos(1, 1)),
        };

        Assert.Null(CombineRecipes.SafeOrder(problem, problem.CurrentCharms, movers));
    }

    /// <summary>계획이 내놓는 자동 적용 목표와 손 이동 안내 둘 다 같은 안전한 순서를 따른다.</summary>
    [Fact]
    public void ThePlanOrdersItsTargetsAndMovesSafely()
    {
        var checkedPlans = 0;
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            var inventory = new InventoryState { Width = 6, Height = 7, Storage = 12 };
            var cells = Enumerable.Range(0, 12).Select(index => new GridPos(index % 6, index / 6)).OrderBy(_ => random.Next()).ToList();
            inventory.Tablets.Add(new PlacedTablet { DefinitionId = Tablet, InstanceId = 1, Position = cells[0], IsApplied = true });
            inventory.Tablets.Add(new PlacedTablet { DefinitionId = Tablet, InstanceId = 2, Position = cells[9], IsApplied = true });
            inventory.Items.Add(new PlacedItem { DefinitionId = Wing, InstanceId = 10, Position = cells[1], IsActive = true });
            inventory.Items.Add(new PlacedItem { DefinitionId = Cloth, InstanceId = 11, Position = cells[2], IsActive = true });
            for (var extra = 0; extra < 6; extra++)
                inventory.Items.Add(new PlacedItem { DefinitionId = extra % 2 == 0 ? Wing + 1 : Cloth + 1, InstanceId = 20 + extra, Position = cells[3 + extra], IsActive = true });
            if (Adjacent(cells[1], cells[2])) continue;
            var snapshot = new GameSnapshot { Inventory = inventory, Run = new RunState { CombineRecipes = new List<List<int>> { new() { Wing, Cloth } } } };

            // 게임이 계산한 칸 레벨이 맞아야 계획이 목표를 낸다. 지금 배치를 한 번 풀어 그 값을 싣는다.
            var seen = PlanBuilder.Build(snapshot, TransitCatalog())!.Current;
            foreach (var level in seen.CellLevels)
                if (level.Value != 0) inventory.LevelMatrix[level.Key.X + "," + level.Key.Y] = level.Value;
            foreach (var item in inventory.Items)
            {
                item.EffectiveLevel = seen.EffectiveLevels[item.Position];
                item.IsActive = !seen.InactiveCells.ContainsKey(item.Position);
            }
            foreach (var tablet in inventory.Tablets) tablet.IsApplied = seen.AppliedTablets[tablet.InstanceId];

            var plan = PlanBuilder.Build(snapshot, TransitCatalog())!;
            if (plan.Targets.Count == 0) continue;
            checkedPlans++;

            var at = plan.Targets.ToDictionary(target => target.InstanceId, target => target.From);
            foreach (var target in plan.Targets)
            {
                var from = at[target.InstanceId];
                foreach (var other in at.Where(pair => pair.Value == target.To && pair.Key != target.InstanceId).Select(pair => pair.Key).ToList())
                    at[other] = from;
                at[target.InstanceId] = target.To;
                Assert.False(Adjacent(at[10], at[11]), $"판 {seed}: 목표 순서 도중에 재료가 맞붙는다");
            }

            var byCell = plan.Targets.ToDictionary(target => target.From, target => target.InstanceId);
            foreach (var move in plan.Moves.Where(move => move.From != move.To))
            {
                var moved = byCell[move.From];
                var swapped = byCell.TryGetValue(move.To, out var occupant);
                byCell[move.To] = moved;
                if (swapped) byCell[move.From] = occupant;
                else byCell.Remove(move.From);
                var wing = byCell.Single(pair => pair.Value == 10).Key;
                var cloth = byCell.Single(pair => pair.Value == 11).Key;
                Assert.False(Adjacent(wing, cloth), $"판 {seed}: 이동 안내 도중에 재료가 맞붙는다");
            }
        }
        Assert.True(checkedPlans > 50, $"목표가 있는 계획 {checkedPlans}");
    }

    private static Catalog TransitCatalog() => new(
        new[] { new TabletDefinition { Id = "T", EntityId = Tablet, Query = "RIGHT 2\nLEFT 1\nBOTTOM 1" } },
        new[]
        {
            new CharmDefinition { Id = "Wing", EntityId = Wing, MaxLevel = 5 },
            new CharmDefinition { Id = "Cloth", EntityId = Cloth, MaxLevel = 5 },
            new CharmDefinition { Id = "A", EntityId = Wing + 1, MaxLevel = 5 },
            new CharmDefinition { Id = "B", EntityId = Cloth + 1, MaxLevel = 5 },
        });
}
