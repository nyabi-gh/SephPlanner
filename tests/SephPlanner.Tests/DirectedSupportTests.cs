using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class DirectedSupportTests
{
    private static PlacementProblem ManaBoard(bool retained = false)
    {
        var p = Board(retained);
        p.FixedEffects.Clear();
        p.Charms[0].Definition.Behavior = "Charm_ReduceMPCost";
        p.Charms[0].Definition.EntityId = 1069;
        p.Charms[0].Definition.MagicSupport = new DirectedMagicSupport
        {
            Effect = MagicSupportEffect.ManaCostReduction,
            OffsetX = -1,
            AmountByLevel = { 25, 50, 100 },
        };
        p.Charms[1].Definition.MagicCostByLevel.AddRange(new double[] { 20, 12 });
        p.CurrentCharms[1] = new GridPos(1, 0);
        p.CurrentCharms[2] = new GridPos(0, 0);
        return p;
    }

    [Fact]
    public void OppositeSupportsCanMoveTogetherWithTheirSharedMagic()
    {
        var p = Board(true);
        p.Grid = new GridSpec(6, 2, 12);
        p.FixedEffects.Clear();
        var reducer = ManaBoard(true).Charms[0];
        reducer.InstanceId = 3;
        p.Charms.Add(reducer);
        p.Charms[1].Definition.MagicCostByLevel.AddRange(new double[] { 20, 12 });
        p.CurrentCharms[3] = new GridPos(2, 0);
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(3, 1), Level = 2 });
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(5, 1), Level = 2 });
        var solved = PlacementSolver.Solve(p);
        Assert.Empty(solved.UnretainedCharms);
        Assert.Empty(solved.UnlinkedCharms);
        Assert.Equal(new GridPos(3, 1), solved.CharmPositions[1]);
        Assert.Equal(new GridPos(4, 1), solved.CharmPositions[2]);
        Assert.Equal(new GridPos(5, 1), solved.CharmPositions[3]);
        foreach (var pair in solved.CharmPositions) p.CurrentCharms[pair.Key] = pair.Value;
        Assert.Equal(solved.CharmPositions.OrderBy(pair => pair.Key),
            PlacementSolver.Solve(p).CharmPositions.OrderBy(pair => pair.Key));
    }

    [Theory]
    [InlineData(12, 25, 0.25)]
    [InlineData(3, 50, 1.0 / 3)]
    [InlineData(12, 100, 1)]
    [InlineData(12, 150, 1)]
    [InlineData(0, 25, 0)]
    [InlineData(12, -25, -0.25)]
    public void ManaSupportUsesRoundedSavingsWithoutInfiniteValue(double cost, double reduction, double ratio)
    {
        var p = ManaBoard();
        p.Charms[0].Definition.MagicSupport!.AmountByLevel[0] = reduction;
        p.Charms[1].Definition.MagicCostByLevel[1] = cost;
        var solved = PlacementSolver.Score(p, new List<TabletPlacement>(), p.CurrentCharms);
        var targetWorth = p.Charms[1].Worth.At(1);
        Assert.Equal(targetWorth * (1 + ratio), solved.Score, 8);
    }

    [Fact]
    public void RetainedManaSupportProtectsTheLeftTargetAndCanMoveWithIt()
    {
        var p = ManaBoard(true);
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(4, 0), Level = 2 });
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(new GridPos(4, 0), solved.CharmPositions[1]);
        Assert.Equal(new GridPos(3, 0), solved.CharmPositions[2]);
        Assert.Empty(solved.UnretainedCharms);
        Assert.Empty(solved.UnlinkedCharms);

        p.FixedEffects.Clear();
        p.Grid = new GridSpec(6, 1, 2);
        Assert.Empty(DiscardAdvisor.Rank(p, PlacementSolver.Solve(p)));
        var ordinary = new OfferCandidate { Charm = new CharmDefinition { EntityId = 9 } };
        Assert.False(OfferAdvisor.Rank(p, new[] { ordinary }, 100)[0].Available);
    }

    [Theory]
    [InlineData("wrongSide")]
    [InlineData("disabled")]
    [InlineData("negative")]
    [InlineData("ordinary")]
    public void ManaSupportDoesNotCountAnUnusableConnection(string failure)
    {
        var p = ManaBoard(true);
        if (failure == "wrongSide")
        {
            p.CurrentCharms[1] = new GridPos(0, 0);
            p.CurrentCharms[2] = new GridPos(1, 0);
        }
        if (failure == "disabled") p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Disable = 1 });
        if (failure == "negative") p.Charms[1].Enchant = -1;
        if (failure == "ordinary") p.Charms[1].Definition.IsMagic = false;
        var solved = PlacementSolver.Score(p, new List<TabletPlacement>(), p.CurrentCharms);
        Assert.Contains(1, solved.UnlinkedCharms);
        Assert.Contains(1, solved.UnretainedCharms);
    }

    private static CharmSlot Helper(int id = 1) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            EntityId = 1168,
            MaxLevel = 2,
            Behavior = "Charm_RightSpellCooldownHelper",
            MagicSupport = new DirectedMagicSupport { OffsetX = 1, AmountByLevel = { 60, 100, 140 } },
        },
    };

    private static CharmSlot Magic(int id = 2) => new()
    {
        InstanceId = id,
        Enchant = 1,
        Definition = new CharmDefinition { EntityId = 3002, IsMagic = true, MaxLevel = 1 },
    };

    private static PlacementProblem Board(bool retained = false)
    {
        var p = new PlacementProblem { Grid = new GridSpec(6, 1, 6) };
        p.Charms.Add(Helper());
        p.Charms[0].Retained = retained;
        p.Charms.Add(Magic());
        p.CurrentCharms[1] = new GridPos(0, 0);
        p.CurrentCharms[2] = new GridPos(1, 0);
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(4, 0), Level = 2 });
        return p;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HelperAndMagicMoveTogetherToTheBetterPairOfCells(bool retained)
    {
        var p = Board(retained);
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(new GridPos(4, 0), solved.CharmPositions[1]);
        Assert.Equal(new GridPos(5, 0), solved.CharmPositions[2]);
        Assert.Empty(solved.UnlinkedCharms);
        Assert.Empty(solved.UnretainedCharms);
        Assert.Equal(solved.Score, PlacementSolver.Score(p, solved.Tablets, solved.CharmPositions).Score, 8);
        foreach (var pair in solved.CharmPositions) p.CurrentCharms[pair.Key] = pair.Value;
        Assert.Equal(solved.CharmPositions.OrderBy(x => x.Key), PlacementSolver.Solve(p).CharmPositions.OrderBy(x => x.Key));
    }

    [Fact]
    public void DisconnectedHelperHasNoStandaloneRarityValue()
    {
        var p = Board();
        var positions = new Dictionary<int, GridPos> { [1] = new(4, 0), [2] = new(1, 0) };
        var solved = PlacementSolver.Score(p, new List<TabletPlacement>(), positions);
        Assert.Equal(p.Charms[1].Worth.At(1), solved.Score, 8);
        Assert.Contains(1, solved.UnlinkedCharms);
        Assert.DoesNotContain(1, solved.InactiveCharms);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(1)]
    [InlineData(10)]
    public void SupportValueUsesTheTargetsWorthAndTheHelpersWeight(double weight)
    {
        var p = Board();
        p.Charms[0].Weight = weight;
        p.Charms[1].Weight = 10;
        var connected = PlacementSolver.Score(p, new List<TabletPlacement>(), p.CurrentCharms);
        var expected = p.Charms[1].Worth.WeightedAt(1, 10) + p.Charms[1].Worth.At(1) * 0.6 * weight;
        Assert.Equal(expected, connected.Score, 8);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("dormant")]
    [InlineData("negative")]
    [InlineData("disabled")]
    [InlineData("criteria")]
    public void RetentionRequiresAnActuallyUsableMagicTarget(string kind)
    {
        var p = Board(true);
        var target = p.Charms[1];
        if (kind == "ordinary") target.Definition.IsMagic = false;
        if (kind == "dormant") target.IsDormant = true;
        if (kind == "negative") target.Enchant = -10;
        if (kind == "disabled") p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(1, 0), Disable = 1 });
        if (kind == "criteria") target.Definition.CriteriaType = "BothSidesAreEmpty";
        var solved = PlacementSolver.Score(p, new List<TabletPlacement>(), p.CurrentCharms);
        Assert.Contains(1, solved.UnretainedCharms);
        Assert.Contains(1, solved.UnlinkedCharms);
    }

    [Fact]
    public void RetainingTheHelperProtectsItsUnretainedTargetFromReplacementAndDiscard()
    {
        var p = Board(true);
        p.Grid = new GridSpec(6, 1, 2);
        p.FixedEffects.Clear();
        var candidate = new OfferCandidate { Charm = new CharmDefinition { EntityId = 9 }, DefinitionId = 9 };
        Assert.False(OfferAdvisor.Rank(p, new[] { candidate }, 100)[0].Available);
        Assert.Empty(DiscardAdvisor.Rank(p, PlacementSolver.Solve(p)));
    }

    [Fact]
    public void TwoHelpersCannotBothRetainTheSameSingleTarget()
    {
        var p = Board(true);
        p.Charms.Add(Helper(3));
        p.Charms[2].Retained = true;
        var solved = PlacementSolver.Solve(p);
        Assert.Single(solved.UnretainedCharms);
        Assert.Single(solved.UnlinkedCharms);
        Assert.Equal(3, solved.CharmPositions.Values.Distinct().Count());
    }

    [Fact]
    public void AnotherUsableMagicCanReplaceTheTargetWithoutBreakingRetention()
    {
        var p = Board(true);
        p.Grid = new GridSpec(6, 1, 2);
        p.FixedEffects.Clear();
        var magic = Magic(9).Definition;
        var advice = OfferAdvisor.Rank(p, new[] { new OfferCandidate { Charm = magic } }, 100)[0];
        Assert.True(advice.Available);
        Assert.Empty(advice.Solved!.UnretainedCharms);
        Assert.Empty(advice.Solved.UnlinkedCharms);
    }

    [Fact]
    public void MissingConnectionDoesNotFalsifyGameActivationButBlocksRetainedPlan()
    {
        var definition = Helper().Definition;
        var snapshot = new GameSnapshot
        {
            Inventory = new InventoryState
            {
                Width = 6,
                Height = 1,
                Storage = 2,
                LevelMatrix = new Dictionary<string, int>(),
                DisabledCells = new List<string>()
            }
        };
        snapshot.Inventory.Items.Add(new PlacedItem
        {
            DefinitionId = definition.EntityId,
            InstanceId = 1,
            Position = new GridPos(0, 0),
            IsActive = true
        });
        var preferences = new PlanPreferences { Recommendations = false, RetainedCharms = { definition.EntityId } };
        var plan = PlanBuilder.Build(snapshot, new Catalog(Array.Empty<TabletDefinition>(), new[] { definition }), preferences)!;
        Assert.True(plan.Verification.Passed);
        Assert.DoesNotContain(1, plan.Current.InactiveCharms);
        Assert.Contains(1, plan.Best.UnretainedCharms);
        Assert.Empty(plan.Targets);
        Assert.Empty(plan.Moves);
        Assert.Contains("사용 가능한 마법", Assert.Single(plan.RetentionWarnings));
    }

    [Fact]
    public void SupportDirectionAndGridBoundaryAreNotInterchangeable()
    {
        var p = Board();
        var wrongSide = new Dictionary<int, GridPos> { [1] = new(1, 0), [2] = new(0, 0) };
        Assert.Contains(1, PlacementSolver.Score(p, new List<TabletPlacement>(), wrongSide).UnlinkedCharms);
        p.Grid = new GridSpec(3, 2, 6);
        var wrapped = new Dictionary<int, GridPos> { [1] = new(2, 0), [2] = new(0, 1) };
        Assert.Contains(1, PlacementSolver.Score(p, new List<TabletPlacement>(), wrapped).UnlinkedCharms);
    }

    [Fact]
    public void TwoPairsOnAFullSmallBoardMatchExhaustiveScoring()
    {
        var p = new PlacementProblem { Grid = new GridSpec(6, 1, 6) };
        p.Charms.AddRange(new[] { Helper(1), Magic(2), Helper(3), Magic(4),
            new CharmSlot { InstanceId = 5, IsFiller = true }, new CharmSlot { InstanceId = 6, IsFiller = true } });
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Level = 2 });
        p.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(3, 0), Level = 1 });
        var positions = new Dictionary<int, GridPos>();
        var best = double.NegativeInfinity;
        void Enumerate(int id)
        {
            if (id == 7) { best = Math.Max(best, PlacementSolver.Score(p, new List<TabletPlacement>(), positions).Preference); return; }
            for (var x = 0; x < 6; x++)
            {
                var cell = new GridPos(x, 0);
                if (positions.ContainsValue(cell)) continue;
                positions[id] = cell;
                Enumerate(id + 1);
                positions.Remove(id);
            }
        }
        Enumerate(1);
        var solved = PlacementSolver.Solve(p);
        Assert.Equal(best, solved.Preference, 8);
        Assert.Empty(solved.UnlinkedCharms);
        Assert.Equal(6, solved.CharmPositions.Values.Distinct().Count());
    }

    [Fact]
    public void CuratedValueStillRequiresAUsableTarget()
    {
        var p = Board();
        p.Charms[0].Worth = new CharmWorth { Source = CharmWorthSource.Curated, Base = 10, PerLevel = 4 };
        var connected = PlacementSolver.Score(p, new List<TabletPlacement>(), p.CurrentCharms);
        Assert.Equal(12, connected.Score, 8);
        var disconnected = new Dictionary<int, GridPos> { [1] = new(4, 0), [2] = new(1, 0) };
        Assert.Equal(2, PlacementSolver.Score(p, new List<TabletPlacement>(), disconnected).Score, 8);
    }
}
