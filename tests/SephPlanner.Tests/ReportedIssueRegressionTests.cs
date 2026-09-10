using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public sealed class ReportedIssueRegressionTests
{
    [Fact]
    public void CrystalDpsIncludesCappedDisabledAndNegativeNeighborsWithoutDoubleCounting()
    {
        var problem = new PlacementProblem { Grid = new(3, 3, 9) };
        problem.Charms.Add(Crystal());
        problem.Charms[0].Enchant = 1;
        for (var id = 2; id <= 5; id++) problem.Charms.Add(Charm(id));
        problem.Charms[1].Enchant = 10;
        problem.Charms[1].Definition.MaxLevel = 2;
        problem.Charms[2].Enchant = -2;
        problem.Charms[3].Enchant = 3;
        problem.Charms[4].IsFiller = true;
        problem.Charms[4].Enchant = 100;
        problem.CurrentCharms[1] = new(1, 1);
        problem.CurrentCharms[2] = new(0, 0);
        problem.CurrentCharms[3] = new(1, 0);
        problem.CurrentCharms[4] = new(2, 0);
        problem.CurrentCharms[5] = new(0, 1);
        problem.FixedEffects.Add(new() { Position = new(2, 0), Disable = 1 });
        Capture(problem, observedBonus: 6);
        var current = PlacementSolver.Score(problem, [], problem.CurrentCharms);
        Assert.Equal(6, current.Combat!.FinalStats["ALLDAMAGEBONUS"]);
        Assert.Equal(106, current.Score);
        var moved = new Dictionary<int, GridPos>(problem.CurrentCharms) { [1] = new(2, 2) };
        var separate = PlacementSolver.Score(problem, [], moved);
        Assert.Equal(100, separate.Score);
        problem.FixedEffects.Add(new() { Position = new(1, 1), Disable = 1 });
        Assert.Equal(100, PlacementSolver.Score(problem, [], problem.CurrentCharms).Score);
    }

    [Fact]
    public void CrystalDpsSearchMatchesEverySmallBoardAssignmentAndDoesNotChurn()
    {
        var problem = new PlacementProblem { Grid = new(6, 1, 6) };
        problem.Charms.Add(Crystal());
        for (var id = 2; id <= 6; id++)
        {
            var charm = Charm(id);
            charm.Enchant = id % 4;
            problem.Charms.Add(charm);
        }
        for (var id = 1; id <= 6; id++) problem.CurrentCharms[id] = new(id - 1, 0);
        Capture(problem, observedBonus: 2);
        var optimum = Exhaustive(problem);
        var solved = PlacementSolver.Solve(problem);
        Assert.Equal(0, PriorityComboPlacement.Compare(solved, optimum));
        Assert.True(solved.Score > 102);
        AssertStable(problem, solved);
    }

    [Theory]
    [InlineData(1157, true)]
    [InlineData(1157, false)]
    [InlineData(1156, true)]
    [InlineData(1156, false)]
    [InlineData(1166, true)]
    [InlineData(1166, false)]
    public void PenaltyItemsKeepAnActiveCellWithAndWithoutWeaponDamage(int definitionId, bool weapon)
    {
        var problem = new PlacementProblem { Grid = new(3, 1, 3) };
        problem.Charms.Add(PenaltyCharm(definitionId));
        problem.CurrentCharms[1] = new(0, 0);
        problem.FixedEffects.Add(new() { Position = new(2, 0), Level = -1 });
        Capture(problem, weapon: weapon);
        var solved = PlacementSolver.Solve(problem);
        Assert.Empty(solved.InactiveCharms);
        Assert.Empty(solved.UnapprovedDeactivations);
        Assert.NotEqual(new GridPos(2, 0), solved.CharmPositions[1]);
        problem.Charms[0].Retained = true;
        Assert.Empty(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)));
        AssertStable(problem, solved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArmorOnlyDeactivatesWithPermissionAndRetentionStillOverridesIt(bool retained)
    {
        var problem = new PlacementProblem { Grid = new(3, 1, 3) };
        problem.Charms.Add(PenaltyCharm(1166));
        problem.Charms[0].AllowDeactivation = true;
        problem.Charms[0].Retained = retained;
        problem.CurrentCharms[1] = new(0, 0);
        problem.FixedEffects.Add(new() { Position = new(2, 0), Level = -1 });
        Capture(problem);
        var solved = PlacementSolver.Solve(problem);
        Assert.Equal(!retained, solved.InactiveCharms.Contains(1));
        Assert.Empty(solved.UnretainedCharms);
        Assert.Equal(retained ? 100 : 103, solved.Score);
    }

    [Fact]
    public void CompetitionDpsLayoutKeepsArmorActiveAndRemovesAvoidableEmptyPenalties()
    {
        var problem = new PlacementProblem { Grid = new(3, 3, 9) };
        problem.Charms.Add(PenaltyCharm(1166));
        problem.Charms.Add(Magic(2));
        problem.Charms[1].Definition.MaxLevel = 1;
        problem.Charms[1].Definition.Combat.Attacks[0].BaseDamage = new() { 100, 200 };
        problem.Tablets.Add(new() { InstanceId = 10, Definition = new() { Query = "DIAUPLEFT -1\nUP -1\nDOWN 3" } });
        problem.CurrentTablets[10] = new(new(1, 1), 0);
        problem.CurrentCharms[1] = new(0, 1);
        problem.CurrentCharms[2] = new(1, 2);
        Capture(problem, weapon: false);
        var solved = PlacementSolver.Solve(problem);
        Assert.Empty(solved.InactiveCharms);
        Assert.Equal(0, solved.UnsafeEmptyCells);
        Assert.Equal(1, Math.Min(solved.Levels[solved.CharmPositions[2]], problem.Charms[1].Definition.MaxLevel));
        Assert.Equal(0, PriorityComboPlacement.Compare(solved, Exhaustive(problem)));
        AssertStable(problem, solved);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HourglassDpsSearchRepairsAndPreservesItsBoltConnection(bool connected, bool useMagic)
    {
        var problem = new PlacementProblem { Grid = new(6, 1, 6) };
        problem.Charms.Add(Hourglass());
        problem.Charms.Add(Magic(2));
        problem.CurrentCharms[1] = new(0, 0);
        problem.CurrentCharms[2] = new(connected ? 1 : 3, 0);
        problem.FixedEffects.Add(new() { Position = new(4, 0), Level = 1 });
        problem.FixedEffects.Add(new() { Position = new(5, 0), Level = 2 });
        Capture(problem, new() { UseMagic = useMagic }, weapon: false);
        var solved = PlacementSolver.Solve(problem);
        Assert.Empty(solved.UnlinkedCharms);
        Assert.Empty(solved.UnpreservedCharms);
        Assert.Equal(solved.CharmPositions[1].Offset(1, 0), solved.CharmPositions[2]);
        if (useMagic)
        {
            var disconnected = PlacementSolver.Score(problem, [], new Dictionary<int, GridPos> { [1] = new(4, 0), [2] = new(3, 0) });
            Assert.True(solved.Score > disconnected.Score);
        }
        AssertStable(problem, solved);
    }

    [Fact]
    public void HourglassDpsRetentionProtectsTheTargetFromDiscardAndNonMagicReplacement()
    {
        var problem = new PlacementProblem { Grid = new(2, 1, 2) };
        problem.Charms.Add(Hourglass());
        problem.Charms[0].Retained = true;
        problem.Charms.Add(Magic(2));
        problem.Charms[1].Definition.Combat.Stats.Add(new() { Key = "PHYSICALDAMAGE", Values = new() { -90 } });
        problem.CurrentCharms[1] = new(0, 0);
        problem.CurrentCharms[2] = new(1, 0);
        Capture(problem, new() { UseMagic = false });
        Assert.Empty(DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem)));
        var candidate = Charm(3).Definition;
        Assert.False(Assert.Single(OfferAdvisor.Rank(problem, new[] { new OfferCandidate { Charm = candidate, DefinitionId = 3 } }, 0)).Available);
    }

    [Theory]
    [InlineData(1289, false)]
    [InlineData(1289, true)]
    [InlineData(1290, false)]
    [InlineData(1290, true)]
    public void PlanetPreferenceReachesDpsPlansWhenBuildPriorityIsEnabled(int definitionId, bool recommendations)
    {
        var needle = Charm(1);
        needle.Definition.EntityId = definitionId;
        needle.Definition.Behavior = "Charm_UpCharmDamage";
        needle.Definition.MaxLevel = 2;
        needle.Definition.DependencyOffsetY = -1;
        needle.Definition.DependencyBonusByLevel = new() { 6, 8, 10 };
        needle.Definition.DependencyExtraByLevel = new() { 15, 20, 25 };
        needle.Definition.HasDependencyCondition = true;
        needle.Definition.DependencyMaxRarity = Rarity.Uncommon;
        var planet = Charm(2);
        planet.Definition.IsAttackable = true;
        planet.Definition.Categories.Add("PLANET");
        planet.Definition.Combat.Unsupported.Add("행성 공격은 미지원입니다.");
        var bolt = Magic(3);
        var inventory = new InventoryState { Width = 2, Height = 2, Storage = 4, ComboCounts = new() { ["PLANET"] = 1 } };
        inventory.Items.Add(new() { DefinitionId = definitionId, InstanceId = 1, Position = new(0, 1), IsActive = true });
        inventory.Items.Add(new() { DefinitionId = 2, InstanceId = 2, Position = new(1, 0), IsActive = true, IsAttackable = true });
        inventory.Items.Add(new() { DefinitionId = 3, InstanceId = 3, Position = new(0, 0), IsActive = true, IsAttackable = true });
        var snapshot = new GameSnapshot { Inventory = inventory, Run = new() { Combat = Snapshot() } };
        var catalog = new Catalog([], new[] { needle.Definition, planet.Definition, bolt.Definition },
            new[] { new ComboDefinition { Id = "PLANET", Thresholds = { 1 }, Combat = new() { Collected = true } } });
        var preferences = new PlanPreferences { Recommendations = recommendations, PriorityCategories = new() { "PLANET" } };
        var dps = PlanBuilder.Build(snapshot, catalog, preferences)!;
        Assert.Equal(dps.Best.CharmPositions[3], dps.Best.CharmPositions[1].Offset(0, -1));
        preferences.Combat.PrioritizeBuild = true;
        var build = PlanBuilder.Build(snapshot, catalog, preferences)!;
        Assert.Equal(build.Best.CharmPositions[2], build.Best.CharmPositions[1].Offset(0, -1));
        Assert.Empty(build.Best.UnlinkedCharms);
        Assert.Empty(build.Best.UnmatchedComboCharms);
        Assert.Contains(build.Best.Combat!.Unsupported, message => message.Contains("행성 공격"));
        Assert.NotEmpty(build.UnsupportedChangeWarnings);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void MixerDpsAdviceDoesNotRequireMerchantOffers(bool merchant, bool used, bool recommendations)
    {
        var charm = Charm(1);
        charm.Definition.Combat.Stats.Add(new() { Key = "PHYSICALDAMAGE", Values = new() { 0, 10, 20 } });
        var tablet = new TabletDefinition { EntityId = 10, Query = "RIGHT 1" };
        var catalog = new Catalog(new[] { tablet }, new[] { charm.Definition, Charm(2).Definition });
        var snapshot = new GameSnapshot
        {
            Run = new() { Gold = 100, Combat = Snapshot() },
            Mixer = new() { Cost = 10, Used = used },
            Inventory = new()
            {
                Width = 3,
                Height = 1,
                Storage = 3,
                Items = { new() { InstanceId = 1, DefinitionId = 1, Position = new(0, 0), IsActive = true } },
                Tablets =
                {
                    new() { InstanceId = 10, DefinitionId = 10, Position = new(1, 0) },
                    new() { InstanceId = 11, DefinitionId = 10, Position = new(2, 0) }
                }
            }
        };
        if (merchant) snapshot.Offers.Add(new() { Kind = "charm", DefinitionId = 2, Price = 5 });
        var plan = PlanBuilder.Build(snapshot, catalog, new() { Recommendations = recommendations })!;
        if (used || !recommendations) Assert.Empty(plan.Mixes);
        else
        {
            var advice = Assert.Single(plan.Mixes);
            Assert.NotNull(advice.Combat);
            Assert.True(advice.Affordable);
        }
        if (!merchant) Assert.Empty(plan.Offers);
    }

    private static CharmSlot Charm(int id) => new()
    {
        InstanceId = id,
        Definition = new() { EntityId = id, Combat = new() { Collected = true } }
    };

    private static CharmSlot Crystal()
    {
        var charm = Charm(1);
        charm.Definition.Behavior = "Charm_NearLevelDamage";
        charm.Definition.MaxLevel = 1;
        charm.Definition.NeighborLevelBonus = new() { 1, 2 };
        return charm;
    }

    private static CharmSlot PenaltyCharm(int definitionId)
    {
        var charm = Charm(1);
        charm.Definition.EntityId = definitionId;
        charm.Definition.Behavior = "Charm_StatusInstance";
        // 로컬 1.0.30 카탈로그의 0레벨 능력치다. 전투 시간·무기 피해는 별도의 시험 조건이다.
        charm.Definition.Combat.Stats = definitionId switch
        {
            1157 => new() { new() { Key = "FINALWEAPONDAMAGE", Values = new() { 4 } }, new() { Key = "EVASION", Values = new() { -400 } } },
            1156 => new() { new() { Key = "FINALWEAPONDAMAGE", Values = new() { 3 } }, new() { Key = "FINALHP", Values = new() { -3 } } },
            1166 => new() { new() { Key = "MAGICCRITICAL", Values = new() { 1000 } }, new() { Key = "MPREGEN", Values = new() { 4 } }, new() { Key = "PHYSICALDAMAGE", Values = new() { -3 } } },
            _ => throw new ArgumentOutOfRangeException(nameof(definitionId))
        };
        charm.Definition.MaxLevel = 0;
        return charm;
    }

    private static CharmSlot Hourglass()
    {
        var charm = Charm(1);
        charm.Definition.Behavior = "Charm_RightSpellCooldownHelper";
        charm.Definition.MaxLevel = 2;
        charm.Definition.MagicSupport = new() { OffsetX = 1, AmountByLevel = new() { 60, 100, 140 } };
        return charm;
    }

    private static CharmSlot Magic(int id)
    {
        var charm = Charm(id);
        charm.Definition.IsMagic = true;
        charm.Definition.IsAttackable = true;
        // 볼트 피해와 재사용 대기는 연결·탐색을 분리해서 확인하기 위한 시험 입력이다.
        charm.Definition.Combat.Attacks.Add(new()
        {
            Id = "bolt",
            Action = CombatActionKind.Magic,
            DamageKind = CombatDamageKind.Bolt,
            BaseDamage = new() { 100 },
            StatPercent = new() { 0 },
            CooldownSeconds = 5
        });
        return charm;
    }

    private static CombatSnapshot Snapshot(bool weapon = true, int observedBonus = 0) => new()
    {
        ObservedStats = new() { ["PHYSICALDAMAGE"] = 100, ["ALLDAMAGEBONUS"] = observedBonus },
        WeaponAttacks = weapon ? new() { new() { Id = "weapon", DamageKind = CombatDamageKind.Weapon } } : new()
    };

    private static void Capture(PlacementProblem problem, CombatScenario? scenario = null, bool weapon = true, int observedBonus = 0)
    {
        var layout = problem.Tablets.Select(tablet => tablet.At(problem.CurrentTablets[tablet.InstanceId].Position,
            problem.CurrentTablets[tablet.InstanceId].Rotation)).ToList();
        var current = PlacementSolver.Score(problem, layout, problem.CurrentCharms);
        problem.Combat = CombatPlanning.Capture(problem, current, Snapshot(weapon, observedBonus), scenario ?? new());
    }

    private static Arrangement Exhaustive(PlacementProblem problem)
    {
        Arrangement? optimum = null;
        var cells = Enumerable.Range(0, problem.Grid.Storage).Select(problem.Grid.ToPosition).ToArray();
        void Assign(int index, List<TabletPlacement> layout, Dictionary<int, GridPos> positions)
        {
            if (index == problem.Charms.Count)
            {
                var scored = PlacementSolver.Score(problem, layout, positions);
                if (optimum == null || PriorityComboPlacement.Compare(scored, optimum) > 0) optimum = scored;
                return;
            }
            foreach (var cell in cells.Where(cell => !positions.ContainsValue(cell) && layout.All(tablet => tablet.Position != cell)))
            {
                positions[problem.Charms[index].InstanceId] = cell;
                Assign(index + 1, layout, positions);
                positions.Remove(problem.Charms[index].InstanceId);
            }
        }
        if (problem.Tablets.Count == 0) Assign(0, [], new());
        else
        {
            var tablet = Assert.Single(problem.Tablets);
            foreach (var cell in cells)
                for (var rotation = 0; rotation < 4; rotation++) Assign(0, new() { tablet.At(cell, rotation) }, new());
        }
        return optimum!;
    }

    private static void AssertStable(PlacementProblem problem, Arrangement solved)
    {
        foreach (var pair in solved.CharmPositions) problem.CurrentCharms[pair.Key] = pair.Value;
        foreach (var pair in solved.TabletPositions) problem.CurrentTablets[pair.Key] = pair.Value;
        var repeated = PlacementSolver.Solve(problem);
        Assert.Equal(solved.Score, repeated.Score, 8);
        Assert.Equal(solved.CharmPositions.OrderBy(pair => pair.Key), repeated.CharmPositions.OrderBy(pair => pair.Key));
        Assert.All(solved.TabletPositions, pair =>
        {
            Assert.Equal(pair.Value.Position, repeated.TabletPositions[pair.Key].Position);
            Assert.Equal(pair.Value.Rotation, repeated.TabletPositions[pair.Key].Rotation);
        });
    }
}
