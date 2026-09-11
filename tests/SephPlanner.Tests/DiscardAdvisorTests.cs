using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class DiscardAdvisorTests
{
    private static PlacementProblem Problem(bool combo = true)
    {
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(5, 1, 5),
            ComboCounts = new Dictionary<string, int> { ["TEST"] = 2 },
            Combos = _ => new ComboDefinition { Id = "TEST", Thresholds = { combo ? 2 : 3 } },
        };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition
            {
                EntityId = 1,
                Id = "자물쇠",
                CriteriaType = "CharmActivateCriteria_BothSidesAreEmpty",
                MaxLevel = 0,
            },
            Worth = new CharmWorth { Base = 20, PerLevel = 0 },
        });
        for (var id = 2; id <= 3; id++)
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Definition = new CharmDefinition { EntityId = id, Id = "일반", MaxLevel = 0, Categories = { "TEST" } },
            });
        foreach (var charm in problem.Charms) problem.CurrentCharms[charm.InstanceId] = new GridPos(charm.InstanceId - 1, 0);
        problem.Tablets.Add(new TabletSlot { InstanceId = 4, Definition = new TabletDefinition { EntityId = 4, Id = "석판" } });
        problem.CurrentTablets[4] = new TabletSpot(new GridPos(4, 0), 0);
        return problem;
    }

    [Fact]
    public void RemovingATabletCanEnableALockWithoutChangingTheInventoryOrLosingACombo()
    {
        var problem = Problem();
        var baseline = PlacementSolver.Solve(problem);
        Assert.Contains(1, baseline.InactiveCharms);

        var advice = DiscardAdvisor.Rank(problem, baseline);

        var tablet = Assert.Single(advice);
        Assert.Equal(4, tablet.InstanceId);
        Assert.True(tablet.IsTablet);
        Assert.Equal(new GridPos(4, 0), tablet.Position);
        Assert.Equal(20, tablet.Gain, 6);
        Assert.Contains("자물쇠", tablet.Activated);
        Assert.False(tablet.ReducesComboCount);
        Assert.Equal(3, problem.Charms.Count);
        Assert.Single(problem.Tablets);
        Assert.Equal(3, baseline.CharmPositions.Count);
        Assert.Single(baseline.TabletPositions);
        Assert.Equal(2, problem.ComboCounts!["TEST"]);
    }

    [Fact]
    public void AnArtifactCanBeSuggestedWhenNoActiveComboStepIsLost()
    {
        var problem = Problem(combo: false);
        var advice = DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem));
        Assert.Contains(advice, a => !a.IsTablet && a.ReducesComboCount && a.Activated.Contains("자물쇠"));
    }

    /// <summary>
    /// 취소되면 절반짜리 목록이 아니라 아무것도 돌아오지 않는다. 돌려주면 그것을 캐시하거나
    /// 화면에 올릴 자리가 생기는데, 중간까지 본 후보 목록은 "이만큼이 전부"라는 뜻이 아니다.
    /// </summary>
    [Fact]
    public void ACancelledSearchDoesNotPublishPartialAdvice()
    {
        var problem = Problem();
        var baseline = PlacementSolver.Solve(problem);
        Assert.Throws<OperationCanceledException>(
            () => DiscardAdvisor.Rank(problem, baseline, cancellation: new CancellationToken(true)));
    }

    [Fact]
    public void RecommendationOffAndUnverifiedSnapshotsDoNotProduceRemovalAdvice()
    {
        var problem = Problem();
        var inventory = new SephPlanner.Core.Runtime.InventoryState { Width = 5, Height = 1, Storage = 5 };
        foreach (var charm in problem.Charms)
            inventory.Items.Add(new SephPlanner.Core.Runtime.PlacedItem
            {
                DefinitionId = charm.Definition.EntityId,
                InstanceId = charm.InstanceId,
                Position = problem.CurrentCharms[charm.InstanceId],
                IsActive = true,
            });
        inventory.Tablets.Add(new SephPlanner.Core.Runtime.PlacedTablet
        {
            DefinitionId = 4,
            InstanceId = 4,
            Position = new GridPos(4, 0),
            IsApplied = true,
        });
        var snapshot = new SephPlanner.Core.Runtime.GameSnapshot { Inventory = inventory };
        var catalog = new Catalog(problem.Tablets.Select(t => t.Definition), problem.Charms.Select(c => c.Definition));
        var plan = PlanBuilder.Build(snapshot, catalog, new PlanPreferences { Recommendations = false })!;
        Assert.Empty(plan.Discards);
        Assert.Equal(4, plan.CreateApplyCommand().Targets.Count);
        var preferences = new PlanPreferences
        {
            CharmValues = new CharmValueBook(new CharmValueFile
            {
                Charms = { new CharmValueEntry { EntityId = 1, Id = "자물쇠", Base = 20, PerLevel = 0 } },
            }),
        };
        plan = PlanBuilder.Build(snapshot, catalog, preferences)!;
        Assert.NotEmpty(plan.Discards);
        Assert.Equal(4, plan.CreateApplyCommand().Targets.Count);
        Assert.Contains(plan.CreateApplyCommand().Targets, t => t.InstanceId == 4);
        inventory.LevelMatrix["0,0"] = 9;
        plan = PlanBuilder.Build(snapshot, catalog)!;
        Assert.False(plan.Verification.Passed);
        Assert.Empty(plan.Discards);
        Assert.Empty(plan.CreateApplyCommand().Targets);
    }

    [Fact]
    public void UnknownComboThresholdsDoNotMakeRemovalLookSafe()
    {
        var problem = Problem();
        problem.Combos = null;
        var baseline = PlacementSolver.Solve(problem);
        Assert.All(DiscardAdvisor.Rank(problem, baseline), a => Assert.True(a.IsTablet));
    }
}
