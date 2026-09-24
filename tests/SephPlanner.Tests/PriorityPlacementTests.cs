using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class PriorityPlacementTests
{
    [Theory]
    [InlineData(1289)]
    [InlineData(1290)]
    public void EitherNeedleHonorsTheSelectedPlanetComboEvenAfterItsThresholdsAreComplete(int definitionId)
    {
        var problem = NeedleProblem(definitionId);
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(result.CharmPositions[2], result.CharmPositions[1].Offset(0, -1));
        Assert.Equal(1, result.PriorityComboMatches);
        Assert.Empty(result.UnlinkedCharms);
        Assert.Empty(result.UnmatchedComboCharms);
    }

    [Theory]
    [InlineData(1289)]
    [InlineData(1290)]
    public void NeedleDoesNotUseAnUnattackablePlanetToSatisfyComboPreference(int definitionId)
    {
        var problem = NeedleProblem(definitionId);
        problem.Charms[1].IsAttackable = false;
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(result.CharmPositions[3], result.CharmPositions[1].Offset(0, -1));
        Assert.Equal(0, result.PriorityComboMatches);
        Assert.Equal(new[] { 1 }, result.UnmatchedComboCharms);
        Assert.Empty(result.UnlinkedCharms);
    }

    private static PlacementProblem NeedleProblem(int definitionId)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 2, 4), PriorityCategories = { "PLANET" } };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition
            {
                EntityId = definitionId,
                MaxLevel = 2,
                Behavior = "Charm_UpCharmDamage",
                DependencyOffsetY = -1,
                DependencyBonusByLevel = { 6, 8, 10 },
                DependencyExtraByLevel = { 15, 20, 25 },
                HasDependencyCondition = true,
                DependencyMaxRarity = Rarity.Uncommon,
            },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Definition = new CharmDefinition
            { EntityId = 1015, MaxLevel = 5, IsAttackable = true, Categories = { "PLANET" } }
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 3,
            Definition = new CharmDefinition
            { EntityId = 3, MaxLevel = 5, IsAttackable = true, Categories = { "OTHER" } }
        });
        problem.CurrentCharms[1] = new GridPos(0, 1);
        problem.CurrentCharms[2] = new GridPos(1, 0);
        problem.CurrentCharms[3] = new GridPos(0, 0);
        problem.ComboCounts = new Dictionary<string, int> { ["PLANET"] = 10, ["OTHER"] = 2 };
        problem.Combos = id => new ComboDefinition { Id = id, Thresholds = { 2 } };
        return problem;
    }

    [Theory]
    [InlineData(1289)]
    [InlineData(1290)]
    public void NeedleLeavesATabletBlockedPositionAndPreservesBothNeedleConnections(int definitionId)
    {
        var problem = NeedleProblem(definitionId);
        problem.Grid = new GridSpec(2, 3, 6);
        problem.Charms.Add(new CharmSlot { InstanceId = 4, Definition = problem.Charms[0].Definition });
        problem.CurrentCharms[3] = new GridPos(1, 1);
        problem.CurrentCharms[4] = new GridPos(0, 2);
        problem.Tablets.Add(new TabletSlot { InstanceId = 10, Definition = new TabletDefinition { EntityId = 10 } });
        problem.CurrentTablets[10] = new TabletSpot(new GridPos(0, 0), 0);
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(2, result.PriorityComboMatches);
        Assert.Empty(result.UnlinkedCharms);
        Assert.Empty(result.UnmatchedComboCharms);
        var neighbors = problem.Charms.ToDictionary(charm => result.CharmPositions[charm.InstanceId]);
        Assert.Equal(3, ComboCounting.CountAll(neighbors)["PLANET"]);
    }

    [Theory]
    [InlineData(1289, true)]
    [InlineData(1289, false)]
    [InlineData(1290, true)]
    [InlineData(1290, false)]
    public void NeedleComboPreferenceReachesPlanBuilderWithRecommendationsOnOrOff(int definitionId, bool recommendations)
    {
        var problem = NeedleProblem(definitionId);
        var inventory = new InventoryState { Width = 2, Height = 2, Storage = 4, ComboCounts = problem.ComboCounts!.ToDictionary(pair => pair.Key, pair => pair.Value) };
        foreach (var charm in problem.Charms)
            inventory.Items.Add(new PlacedItem
            {
                InstanceId = charm.InstanceId,
                DefinitionId = charm.Definition.EntityId,
                Position = problem.CurrentCharms[charm.InstanceId],
                IsActive = true,
                IsAttackable = charm.Definition.IsAttackable
            });
        var catalog = new Catalog(Array.Empty<TabletDefinition>(), problem.Charms.Select(charm => charm.Definition));
        var plan = PlanBuilder.Build(new GameSnapshot { Inventory = inventory }, catalog,
            new PlanPreferences { Recommendations = recommendations, PriorityCategories = { "PLANET" } })!;
        Assert.True(plan.Verification.Passed);
        Assert.Equal(plan.Best.CharmPositions[2], plan.Best.CharmPositions[1].Offset(0, -1));
        Assert.Equal(1, plan.Best.PriorityComboMatches);
        Assert.Empty(plan.Best.UnlinkedCharms);
    }

    private static CharmDefinition Key() => new()
    {
        EntityId = 10,
        Id = "KEY",
        MaxLevel = 20,
        LineCategories = { "STURDY", "EMBER", "GLACIER", "MAGITECH" },
    };

    private static PlacementProblem KeyProblem()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 4, 4) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = Key(), Weight = 10000 });
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Level = 10 });
        problem.ComboCounts = new Dictionary<string, int> { ["STURDY"] = 1 };
        problem.Combos = id => new ComboDefinition { Id = id, Thresholds = { 4 } };
        return problem;
    }

    [Fact]
    public void SelectionOutranksEvenLargeLevelGainsWithoutInflatingTheDisplayedScore()
    {
        var problem = KeyProblem();
        var normal = PlacementSolver.Solve(problem);
        Assert.Equal(new GridPos(0, 0), normal.CharmPositions[1]);

        problem.PriorityCategories.Add("GLACIER");
        var chosen = PlacementSolver.Solve(problem);
        Assert.Equal(new GridPos(0, 2), chosen.CharmPositions[1]);
        Assert.Equal(1, chosen.PriorityComboMatches);
        Assert.Empty(chosen.UnmatchedComboCharms);
        Assert.True(chosen.Score < normal.Score);

        problem.PriorityCategories.Clear();
        var unweighted = PlacementSolver.Score(problem, chosen.Tablets, chosen.CharmPositions);
        Assert.Equal(unweighted.Score, chosen.Score);
        Assert.Equal(normal.CharmPositions[1], PlacementSolver.Solve(problem).CharmPositions[1]);
    }

    [Fact]
    public void SelectionStillAppliesWhenAllComboThresholdsAreAlreadyComplete()
    {
        var problem = KeyProblem();
        problem.ComboCounts = new Dictionary<string, int> { ["GLACIER"] = 20, ["STURDY"] = 1 };
        problem.PriorityCategories.Add("GLACIER");
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(new GridPos(0, 2), result.CharmPositions[1]);
        Assert.Equal(0, result.PriorityComboProgress);
    }

    [Fact]
    public void AmongSelectedCombosCompletionComesBeforeOrdinaryPlacementValue()
    {
        var problem = KeyProblem();
        problem.PriorityCategories.UnionWith(new[] { "EMBER", "GLACIER" });
        problem.ComboCounts = new Dictionary<string, int> { ["EMBER"] = 1, ["GLACIER"] = 3, ["STURDY"] = 1 };
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 1), Level = 10 });
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(new GridPos(0, 2), result.CharmPositions[1]);
        Assert.Equal(Worth.ComboThreshold, result.PriorityComboProgress);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImpossibleSelectionFallsBackAndReportsTheUnmatchedKey(bool unavailableCategory)
    {
        var problem = KeyProblem();
        problem.PriorityCategories.Add(unavailableCategory ? "UNKNOWN" : "GLACIER");
        if (!unavailableCategory)
            problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 2), Disable = 1 });
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(new GridPos(0, 0), result.CharmPositions[1]);
        Assert.Equal(new[] { 1 }, result.UnmatchedComboCharms);
        Assert.Equal(0, result.PriorityComboMatches);
    }

    [Fact]
    public void ExistingCriteriaHoldIsPreservedWhenItConflictsWithTheChosenCombo()
    {
        var problem = KeyProblem();
        problem.Charms[0].Held = true;
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), IgnoreCriteria = 1 });
        problem.PriorityCategories.Add("GLACIER");
        var result = PlacementSolver.Solve(problem);
        Assert.Empty(result.UnheldCharms);
        Assert.Equal(new[] { 1 }, result.UnmatchedComboCharms);
    }

    private static PlacementProblem PaperProblem()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(3, 2, 6) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition { EntityId = 1, Id = "PAPER", Behavior = "Charm_WhitePaper" },
        });
        for (var id = 2; id <= 3; id++)
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Definition = new CharmDefinition { EntityId = id, Id = "EMBER" + id, Categories = { "EMBER" } },
            });
        problem.Charms.Add(new CharmSlot { InstanceId = 4, IsFiller = true });
        problem.Tablets.Add(new TabletSlot { InstanceId = 5, Definition = new TabletDefinition() });
        problem.Tablets.Add(new TabletSlot { InstanceId = 6, Definition = new TabletDefinition() });
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.CurrentCharms[2] = new GridPos(0, 1);
        problem.CurrentCharms[3] = new GridPos(2, 1);
        problem.CurrentCharms[4] = new GridPos(2, 0);
        problem.CurrentTablets[5] = new TabletSpot(new GridPos(1, 0), 0);
        problem.CurrentTablets[6] = new TabletSpot(new GridPos(1, 1), 0);
        problem.ComboCounts = new Dictionary<string, int> { ["EMBER"] = 2 };
        problem.Combos = id => new ComboDefinition { Id = id, Thresholds = { 3 } };
        problem.PriorityCategories.Add("EMBER");
        return problem;
    }

    [Fact]
    public void PaperBuildsATripleAndMovesBlockingTabletsInAFullBag()
    {
        var problem = PaperProblem();
        var layout = problem.Tablets.Select(slot => slot.At(problem.CurrentTablets[slot.InstanceId].Position, 0)).ToList();
        var result = PlacementSolver.EvaluateLayouts(problem, new[] { layout });
        Assert.Equal(1, result.PriorityComboMatches);
        Assert.Empty(result.UnmatchedComboCharms);
        Assert.Equal(6, result.CharmPositions.Values.Concat(result.Tablets.Select(tablet => tablet.Position)).Distinct().Count());
        Assert.Equal(0, result.UnplacedTablets);
        var paper = result.CharmPositions[1];
        Assert.Equal(1, paper.X);
        Assert.Contains(paper.Offset(-1, 0), new[] { result.CharmPositions[2], result.CharmPositions[3] });
        Assert.Contains(paper.Offset(1, 0), new[] { result.CharmPositions[2], result.CharmPositions[3] });

        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var pair in result.CharmPositions) problem.CurrentCharms[pair.Key] = pair.Value;
            foreach (var pair in result.TabletPositions) problem.CurrentTablets[pair.Key] = pair.Value;
            var neighbors = problem.Charms.ToDictionary(charm => result.CharmPositions[charm.InstanceId]);
            problem.ComboCounts = ComboCounting.CountAll(neighbors);
            problem.BaseComboCounts = null;
            var next = PlacementSolver.Solve(problem);
            Assert.Equal(result.CharmPositions.OrderBy(pair => pair.Key), next.CharmPositions.OrderBy(pair => pair.Key));
            Assert.Equal(result.TabletPositions.OrderBy(pair => pair.Key), next.TabletPositions.OrderBy(pair => pair.Key));
            result = next;
        }
    }

    [Fact]
    public void APaperCannotUseTheSameNeighborTwice()
    {
        var problem = PaperProblem();
        problem.Charms[2].Definition.Categories.Clear();
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(new[] { 1 }, result.UnmatchedComboCharms);
        Assert.Contains("양옆 아티팩트 둘", PriorityPlacement.FailureReason(problem, problem.Charms[0]));
    }

    [Fact]
    public void AnUnopenedKeyRowHasASpecificExplanation()
    {
        var problem = KeyProblem();
        problem.Grid = new GridSpec(1, 4, 2);
        problem.PriorityCategories.Add("GLACIER");
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(new[] { 1 }, result.UnmatchedComboCharms);
        Assert.Contains("행이 아직 열리지", PriorityPlacement.FailureReason(problem, problem.Charms[0]));
    }

    [Fact]
    public void PaperAndKeyCanSatisfyTheSelectionTogether()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(3, 4, 12) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = Key() });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Definition = new CharmDefinition { Behavior = "Charm_WhitePaper" },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 3,
            Definition = new CharmDefinition { Categories = { "GLACIER" } },
        });
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.CurrentCharms[2] = new GridPos(0, 1);
        problem.CurrentCharms[3] = new GridPos(2, 0);
        problem.PriorityCategories.Add("GLACIER");
        var result = PlacementSolver.Solve(problem);
        Assert.Equal(2, result.PriorityComboMatches);
        Assert.Empty(result.UnmatchedComboCharms);
        Assert.Equal(new GridPos(1, 2), result.CharmPositions[2]);
    }

    [Fact]
    public void CachedBaselineIsRecomputedWhenSelectionChanges()
    {
        var problem = KeyProblem();
        var cache = new LayoutCache();
        var options = new SolverOptions();
        var before = cache.Baseline(problem, options);
        problem.PriorityCategories.Add("GLACIER");
        var after = cache.Baseline(problem, options);
        Assert.NotSame(before, after);
        Assert.Equal(new GridPos(0, 2), after.CharmPositions[1]);
    }

    [Fact]
    public void OfferPreviewUsesTheSameSelectedComboAsTheCurrentInventoryPlan()
    {
        var key = Key();
        var offered = new CharmDefinition { EntityId = 11, Id = "OFFER", MaxLevel = 5 };
        var catalog = new Catalog(Array.Empty<TabletDefinition>(), new[] { key, offered });
        var inventory = new InventoryState { Width = 1, Height = 4, Storage = 4 };
        inventory.Items.Add(new PlacedItem { DefinitionId = key.EntityId, InstanceId = 1, IsActive = true });
        var snapshot = new GameSnapshot { Inventory = inventory };
        snapshot.Offers.Add(new OfferedItem { Kind = "charm", DefinitionId = 11 });
        var plan = PlanBuilder.Build(snapshot, catalog, new PlanPreferences { PriorityCategories = { "GLACIER" } })!;
        Assert.Equal(new GridPos(0, 2), plan.Best.CharmPositions[1]);
        Assert.Equal(new GridPos(0, 2), plan.Offers.Single().Preview!.Charms.Single(pair => pair.Value == 10).Key);
    }

    [Fact]
    public void PlanBuilderKeepsTheSelectedComboEvenWhenTheRawScoreFallsAndRecommendationsAreOff()
    {
        var key = Key();
        var catalog = new Catalog(Array.Empty<TabletDefinition>(), new[] { key });
        var inventory = new InventoryState { Width = 1, Height = 4, Storage = 4 };
        inventory.Items.Add(new PlacedItem { DefinitionId = key.EntityId, InstanceId = 1, IsActive = true, EffectiveLevel = 10 });
        inventory.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Level = 10 });
        inventory.LevelMatrix["0,0"] = 10;
        var plan = PlanBuilder.Build(new GameSnapshot { Inventory = inventory }, catalog,
            new PlanPreferences { Recommendations = false, PriorityCategories = { "GLACIER" } })!;
        Assert.True(plan.Verification.Passed);
        Assert.True(plan.Gain < 0);
        Assert.True(plan.HasPlacementChanges);
        Assert.Equal(new GridPos(0, 2), plan.Targets.Single().To);
    }

    /// <summary>
    /// 콤보 우선은 점수보다 앞서므로, 번호 없는 아이템을 옮겨 종이를 끼운 배치가 이기면 자동 배치가
    /// 그 칸에서 멈춘다. 못 옮기는 아이템은 제자리여야 한다.
    /// </summary>
    [Fact]
    public void ThePriorityComboNeverMovesAnImmovableItem()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(4, 1, 4), PriorityCategories = { "X" } };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = new CharmDefinition { Id = "a", EntityId = 1, MaxLevel = 5, Categories = { "X" } } });
        problem.Charms.Add(new CharmSlot { InstanceId = -2, IsFiller = true, Immovable = true, Definition = new CharmDefinition { Id = "f", EntityId = 2 } });
        problem.Charms.Add(new CharmSlot { InstanceId = 3, Definition = new CharmDefinition { Id = "p", EntityId = 3, MaxLevel = 5, Behavior = "Charm_WhitePaper" } });
        problem.Charms.Add(new CharmSlot { InstanceId = 4, Definition = new CharmDefinition { Id = "b", EntityId = 4, MaxLevel = 5, Categories = { "X" } } });
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.CurrentCharms[-2] = new GridPos(1, 0);
        problem.CurrentCharms[3] = new GridPos(2, 0);
        problem.CurrentCharms[4] = new GridPos(3, 0);

        var best = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(1, 0), best.CharmPositions[-2]);
    }
}
