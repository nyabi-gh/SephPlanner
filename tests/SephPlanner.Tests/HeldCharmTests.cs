using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 제한 해제 칸 고정. 자물쇠처럼 조건이 까다로운 아티팩트는 빈 칸이 많을 때 양옆을 비우는 공짜
/// 자리로 가고 가방이 차면 옮겨 다닌다. 사용자가 "이건 조건 무시 칸에 두라"고 하면 그 칸을 다른
/// 아티팩트에게 내주지 않는다. 그런 칸이 없으면 계획을 포기하지 않고 알린다.
/// </summary>
public class HeldCharmTests
{
    private const int TabletEntity = 100;
    private const int StrongEntity = 200;
    private const int LockEntity = 201;

    /// <summary>
    /// 석판이 (1,0) 에 +2 를 주고 배치 조건을 무시하게 한다. STRONG 은 레벨 하나에 5, LOCK 은 1 이고
    /// LOCK 은 양옆이 비어야 켜진다. 고정이 없으면 STRONG 이 (1,0) 을 받고 LOCK 은 빈 칸 사이로 간다.
    /// </summary>
    private static PlacementProblem Board(bool ignoreCell, bool held)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 1, 6) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 1,
            Definition = new TabletDefinition
            {
                Id = "T",
                EntityId = TabletEntity,
                Query = ignoreCell ? "RIGHT 2\nRIGHT IGNORECRITERIA" : "RIGHT 2",
            },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            Definition = new CharmDefinition { Id = "STRONG", EntityId = StrongEntity, MaxLevel = 5 },
            Worth = new CharmWorth { Base = 1.5, PerLevel = 5 },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 11,
            Definition = new CharmDefinition
            {
                Id = "LOCK",
                EntityId = LockEntity,
                MaxLevel = 5,
                CriteriaType = "BothSidesAreEmpty",
            },
            Worth = new CharmWorth { Base = 1.5, PerLevel = 1 },
            Held = held,
        });
        problem.CurrentTablets[1] = new TabletSpot(new GridPos(0, 0), 0);
        problem.CurrentCharms[10] = new GridPos(1, 0);
        problem.CurrentCharms[11] = new GridPos(4, 0);
        return problem;
    }

    [Fact]
    public void WithoutAHoldTheIgnoreCellGoesToTheMoreValuableCharm()
    {
        var arrangement = PlacementSolver.Solve(Board(ignoreCell: true, held: false));

        // 석판의 +2 와 조건 무시는 같은 칸에 걸린다. 그 칸을 누가 받았는지는 레벨이 말한다.
        Assert.Equal(2, arrangement.CellLevels[arrangement.CharmPositions[10]]);
        Assert.Empty(arrangement.UnheldCharms);
    }

    [Fact]
    public void AHeldCharmTakesTheIgnoreCellEvenFromAMoreValuableCharm()
    {
        var arrangement = PlacementSolver.Solve(Board(ignoreCell: true, held: true));

        // 석판을 옮기거나 돌리는 쪽(한 수)이 아티팩트 둘을 맞바꾸는 쪽(두 수)보다 싸므로, 조건 무시
        // 칸이 어디가 되는지는 답의 일부다(실제로 석판을 180도 돌려 자물쇠 자리에 맞췄다). 그 칸에는
        // +2 도 같이 걸리므로 누가 받았는지는 레벨이 말한다.
        Assert.Equal(2, arrangement.CellLevels[arrangement.CharmPositions[11]]);
        Assert.Equal(0, arrangement.CellLevels[arrangement.CharmPositions[10]]);
        Assert.DoesNotContain(11, arrangement.InactiveCharms);
        Assert.Empty(arrangement.UnheldCharms);
    }

    [Fact]
    public void TheReportedScoreDoesNotCarryTheHoldPenalty()
    {
        // 고정은 배치를 고를 때만 힘을 쓴다. 점수에 섞이면 화면의 숫자가 수백 점씩 튄다.
        var held = PlacementSolver.Solve(Board(ignoreCell: true, held: true));
        var free = PlacementSolver.Solve(Board(ignoreCell: true, held: false));

        Assert.True(held.Score > 0 && held.Score < free.Score + 1e-9,
            $"고정 {held.Score} / 자유 {free.Score}");
    }

    [Fact]
    public void WithNoIgnoreCellThePlanStillComesOutAndSaysTheHoldWasNotKept()
    {
        var arrangement = PlacementSolver.Solve(Board(ignoreCell: false, held: true));

        Assert.Equal(new[] { 11 }, arrangement.UnheldCharms);
        Assert.True(arrangement.CharmPositions.ContainsKey(11));
        Assert.Equal(2, arrangement.CellLevels[arrangement.CharmPositions[10]]);
    }

    [Fact]
    public void AHoldReachesTheSolverThroughPreferencesAndTheFingerprint()
    {
        var catalog = new Catalog(
            new[]
            {
                new TabletDefinition
                {
                    Id = "T", EntityId = TabletEntity, Query = "RIGHT 2\nRIGHT IGNORECRITERIA",
                },
            },
            new[]
            {
                new CharmDefinition { Id = "STRONG", EntityId = StrongEntity, MaxLevel = 5 },
                new CharmDefinition
                {
                    Id = "LOCK", EntityId = LockEntity, MaxLevel = 5, CriteriaType = "BothSidesAreEmpty",
                },
            });
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
            Position = new GridPos(1, 0),
            IsActive = true,
        });
        inventory.Items.Add(new PlacedItem
        {
            DefinitionId = LockEntity,
            InstanceId = 11,
            Position = new GridPos(4, 0),
            IsActive = true,
        });
        var snapshot = new GameSnapshot { Inventory = inventory, Run = new RunState() };
        var values = new CharmValueBook(new CharmValueFile
        {
            Version = 1,
            Charms =
            {
                new CharmValueEntry { Id = "STRONG", EntityId = StrongEntity, Base = 1.5, PerLevel = 5 },
                new CharmValueEntry { Id = "LOCK", EntityId = LockEntity, Base = 1.5, PerLevel = 1 },
            },
        });

        var plain = new PlanPreferences { CharmValues = values };
        var holding = new PlanPreferences { CharmValues = values, HeldCharms = { LockEntity } };

        var plan = PlanBuilder.Build(snapshot, catalog, holding)!;
        Assert.Equal(2, plan.Best.CellLevels[plan.Best.CharmPositions[11]]);
        Assert.Empty(plan.Best.UnheldCharms);

        // 지정을 바꾸면 계획의 지문도 바뀌어야 F8 이 낡은 계획을 적용하지 않는다.
        Assert.NotEqual(
            PlanFingerprint.PlanningContext(plain, "g"),
            PlanFingerprint.PlanningContext(holding, "g"));
    }
}
