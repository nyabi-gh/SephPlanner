using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 신비 콤보 각인은 콤보 수를 따라 서버가 심었다 지운다. 제보 <c>ca26eeb4</c> 에서는 신비 4개일 때의
/// ×2 세 칸을 고정 효과로 받아, 하얀 종이를 옮겨 신비를 3개로 떨어뜨리는 배치에 사라질 두 칸을
/// 믿고 아티팩트를 놓았다.
/// </summary>
public class ComboEngravingTests
{
    /// <summary>게임 프리팹 값과 같은 단계(2 에 1칸, 4 에 2칸 더).</summary>
    private static ComboEngravingRule Mystic(params GridPos[] positions)
    {
        var rule = new ComboEngravingRule { Category = "MYSTIC", Query = "O MUL/2" };
        rule.Tiers.Add(new ComboEngravingTier { Threshold = 2, Count = 1 });
        rule.Tiers.Add(new ComboEngravingTier { Threshold = 4, Count = 2 });
        rule.Positions.AddRange(positions);
        return rule;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 3)]
    [InlineData(9, 3)]
    public void EachThresholdPlantsTheNextPositions(int count, int cells)
    {
        var grid = new GridSpec(6, 5, 29);
        var rule = Mystic(new GridPos(5, 1), new GridPos(2, 4), new GridPos(0, 2), new GridPos(3, 3));

        var planted = ComboEngravings.Cells(rule, ComboEngravings.StageAt(rule, count), grid);

        Assert.Equal(cells, planted.Count);
        Assert.All(planted, cell => Assert.Equal(2, cell.Multiply));
        var expected = new[] { new GridPos(5, 1), new GridPos(2, 4), new GridPos(0, 2) }.Take(cells);
        Assert.Equal(expected, planted.Select(cell => cell.Position));
    }

    [Fact]
    public void MissingPositionsAreNotInvented()
    {
        var rule = Mystic(new GridPos(1, 0));

        var planted = ComboEngravings.Cells(rule, ComboEngravings.StageAt(rule, 4), new GridSpec(3, 1, 3));

        Assert.Equal(new[] { new GridPos(1, 0) }, planted.Select(cell => cell.Position));
    }

    [Fact]
    public void TheResidualLosesExactlyTheLiveEngravings()
    {
        var residual = new FixedEffectResidualResult
        {
            Status = FixedEffectResidualStatus.Extracted,
            Cells =
            {
                new FixedEffectCell { Position = new GridPos(5, 1), Multiply = 2 },
                new FixedEffectCell { Position = new GridPos(2, 4), Multiply = 4 },
                new FixedEffectCell { Position = new GridPos(0, 0), Level = 3 },
            },
        };
        var engraved = new List<FixedEffectCell>
        {
            new() { Position = new GridPos(5, 1), Multiply = 2 },
            new() { Position = new GridPos(2, 4), Multiply = 2 },
        };

        var permanent = ComboEngravings.Without(residual, engraved);

        Assert.Equal(FixedEffectResidualStatus.Extracted, permanent.Status);
        Assert.True(FixedEffectResidual.Same(permanent.Cells, new List<FixedEffectCell>
        {
            new() { Position = new GridPos(2, 4), Multiply = 2 },
            new() { Position = new GridPos(0, 0), Level = 3 },
        }));
    }

    /// <summary>콤보 수와 행렬은 따로 동기화된다. 아직 심기지 않은 각인은 어긋남이 아니라 기다릴 일이다.</summary>
    [Fact]
    public void AnEngravingTheMatrixDoesNotShowYetIsUnsettled()
    {
        var residual = new FixedEffectResidualResult { Status = FixedEffectResidualStatus.Extracted };
        var engraved = new List<FixedEffectCell> { new() { Position = new GridPos(1, 0), Multiply = 2 } };

        Assert.Equal(FixedEffectResidualStatus.Unsettled, ComboEngravings.Without(residual, engraved).Status);
    }

    /// <summary>
    /// 1x5 격자. 신비 둘 사이의 하얀 종이가 신비를 3개로 만들어 문턱 3을 넘기고, 그 단계의 각인이
    /// (4,0) 에 ×2 를 심는다. 종이가 사이를 떠나면 각인도 사라져야 한다.
    /// </summary>
    private static PlacementProblem Sandwich()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(5, 1, 5) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = new CharmDefinition { MaxLevel = 5, Categories = { "MYSTIC" } } });
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = new CharmDefinition { MaxLevel = 5, Behavior = "Charm_WhitePaper" } });
        problem.Charms.Add(new CharmSlot { InstanceId = 3, Definition = new CharmDefinition { MaxLevel = 5, Categories = { "MYSTIC" } } });
        problem.Charms.Add(new CharmSlot { InstanceId = 4, Enchant = 1, Definition = new CharmDefinition { MaxLevel = 5 } });
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.CurrentCharms[2] = new GridPos(1, 0);
        problem.CurrentCharms[3] = new GridPos(2, 0);
        problem.CurrentCharms[4] = new GridPos(4, 0);
        problem.ComboCounts = new Dictionary<string, int> { ["MYSTIC"] = 3 };

        var rule = new ComboEngravingRule { Category = "MYSTIC", Query = "O MUL/2", Positions = { new GridPos(4, 0) } };
        rule.Tiers.Add(new ComboEngravingTier { Threshold = 3, Count = 1 });
        problem.ComboEngraving = rule;
        return problem;
    }

    [Fact]
    public void BreakingTheComboRemovesItsEngravingFromTheScore()
    {
        var problem = Sandwich();
        var none = new List<TabletPlacement>();

        var kept = PlacementSolver.Score(problem, none, problem.CurrentCharms);
        var broken = PlacementSolver.Score(problem, none, new Dictionary<int, GridPos>
        {
            [1] = new GridPos(0, 0),
            [2] = new GridPos(3, 0),
            [3] = new GridPos(2, 0),
            [4] = new GridPos(4, 0),
        });

        Assert.Equal(2, kept.CellLevels[new GridPos(4, 0)]);
        Assert.Equal(1, broken.CellLevels[new GridPos(4, 0)]);
    }

    /// <summary>
    /// 계획이 기대한 칸 레벨은 그 배치에서 게임이 셀 콤보 수로 심을 각인과 맞아야 한다. 자동 배치
    /// 뒤의 레벨 대조가 바로 이것이라, 어긋나면 "계산에 없는 효과" 로 제보된다.
    /// </summary>
    [Fact]
    public void ASolvedLayoutExpectsOnlyTheEngravingsItsOwnCountPlants()
    {
        var flipped = 0;
        for (var seed = 0; seed < 40; seed++)
        {
            var problem = MysticBoard(seed);
            var rule = problem.ComboEngraving!;
            var best = PlacementSolver.Solve(problem);

            var count = CountAt(problem, best.CharmPositions);
            var stage = ComboEngravings.StageAt(rule, count);
            if (stage != ComboEngravings.StageAt(rule, problem.ComboCounts!["MYSTIC"])) flipped++;

            var fixedOnly = MysticBoard(seed);
            fixedOnly.ComboEngraving = null;
            fixedOnly.FixedEffects.AddRange(ComboEngravings.Cells(rule, stage, problem.Grid));
            var rescored = PlacementSolver.Score(fixedOnly, best.Tablets, best.CharmPositions);

            Assert.Equal(rescored.CellLevels.OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X),
                best.CellLevels.OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X));
        }

        // 단계가 바뀌는 판이 하나도 없으면 이 대조는 아무것도 검사하지 않은 것이다.
        Assert.True(flipped > 0);
    }

    /// <summary>
    /// 다듬기의 증분 경로도 단계를 따라가야 한다. 교환이 조건 칸을 건드리지 않아도 콤보 수를
    /// 바꾸면 행렬이 달라지므로, 전체 재계산과의 대조가 그 자리를 잡는다.
    /// </summary>
    [Fact]
    public void TheIncrementalPolishFollowsTheEngravingStage()
    {
        for (var seed = 0; seed < 40; seed++)
        {
            var verified = PlacementSolver.Solve(MysticBoard(seed), new SolverOptions { VerifyIncrementalPolish = true });
            var plain = PlacementSolver.Solve(MysticBoard(seed), new SolverOptions());

            Assert.Equal(plain.Score, verified.Score, 12);
            Assert.Equal(plain.CharmPositions.OrderBy(pair => pair.Key).ToList(),
                verified.CharmPositions.OrderBy(pair => pair.Key).ToList());
        }
    }

    /// <summary>
    /// 가방이 차서 후보를 넣으려고 신비 하나를 밀어내는 갈래. 게임 수는 밀려난 것까지 센 값이라
    /// 갈래가 덜어 내지 않으면 신비 4개로 남아 사라질 ×2 칸을 믿는다.
    /// </summary>
    [Fact]
    public void DisplacingAMysticForAnOfferLowersTheEngravingStage()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(4, 1, 4) };
        for (var id = 1; id <= 4; id++)
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Definition = new CharmDefinition { Id = "m" + id, EntityId = 100 + id, MaxLevel = 5, Categories = { "MYSTIC" } },
            });
            problem.CurrentCharms[id] = new GridPos(id - 1, 0);
        }
        problem.ComboCounts = new Dictionary<string, int> { ["MYSTIC"] = 4 };
        problem.ComboEngraving = Mystic(new GridPos(0, 0), new GridPos(1, 0), new GridPos(2, 0));

        var offer = new OfferCandidate { DefinitionId = 900, Kind = "charm", Charm = new CharmDefinition { Id = "y", EntityId = 900, MaxLevel = 5 } };
        var displaced = OfferAdvisor.Trials(problem, offer, -1000, null)
            .Single(outcome => outcome.DisplacedCharm?.InstanceId == 1).Trial;

        Assert.Equal(3, ComboCounting.Reported(displaced, "MYSTIC"));
        var after = displaced.Charms.Where(charm => displaced.CurrentCharms.ContainsKey(charm.InstanceId))
            .ToDictionary(charm => displaced.CurrentCharms[charm.InstanceId], charm => charm);
        after[new GridPos(0, 0)] = displaced.Charms.Single(charm => charm.InstanceId == -1000);
        Assert.Single(displaced.EffectsFor(after));
    }

    /// <summary>
    /// 빔은 콤보 각인을 단계로만 본다. 같은 단계 안에서 수만 바뀌었는데 빔을 다시 찾으면 종이를
    /// 옮길 때마다 cold 가 된다.
    /// </summary>
    [Fact]
    public void TheBeamIsKeptWhileTheCountStaysInOneStage()
    {
        var cache = new LayoutCache();
        var options = SolverOptions.ForAdvice(default);

        cache.Of(BeamBoard(4), options);
        cache.Of(BeamBoard(5), options);
        Assert.Equal(1, cache.Searches);

        cache.Of(BeamBoard(3), options);
        Assert.Equal(2, cache.Searches);
    }

    private static PlacementProblem BeamBoard(int mystic)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(3, 2, 6) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 900,
            Definition = new TabletDefinition { Id = "t", EntityId = 700, Query = "RIGHT 1" },
        });
        problem.CurrentTablets[900] = new TabletSpot(new GridPos(0, 0), 0);
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = new CharmDefinition { Id = "a", EntityId = 1, MaxLevel = 5 } });
        problem.CurrentCharms[1] = new GridPos(1, 0);
        problem.ComboCounts = new Dictionary<string, int> { ["MYSTIC"] = mystic };
        problem.ComboEngraving = Mystic(new GridPos(2, 1), new GridPos(0, 1), new GridPos(1, 1));
        return problem;
    }

    private static ComboDefinition? MysticCombo(string id) =>
        id == "MYSTIC" ? new ComboDefinition { Id = "MYSTIC", Thresholds = { 2, 3, 4, 5, 6 } } : null;

    /// <summary>
    /// 신비 후보의 ×2 칸은 갈래의 배치 점수가 이미 센다. 일반 콤보 보너스까지 얹으면 같은 이득을
    /// 두 번 받는다. 문구와 완성 표시는 그대로 두고, F2 로 고른 콤보는 사용자 선택이라 남긴다.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void AMysticOfferEarnsNoGenericComboBonusWhenItsCellsAreScored(bool rule, bool priority, bool bonus)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 1, 6) };
        if (rule) problem.ComboEngraving = Mystic(new GridPos(0, 0), new GridPos(1, 0), new GridPos(2, 0));
        var offer = new OfferCandidate
        {
            DefinitionId = 900,
            Kind = "charm",
            Name = "m",
            Charm = new CharmDefinition { Id = "m", EntityId = 900, MaxLevel = 5, Categories = { "MYSTIC" } },
        };
        var counts = new Dictionary<string, int> { ["MYSTIC"] = 1 };

        var advice = OfferAdvisor.Rank(problem, new List<OfferCandidate> { offer }, int.MaxValue, counts, MysticCombo,
            priority ? new[] { "MYSTIC" } : null).Single();

        Assert.True(advice.ComboCompletes);
        Assert.Equal(bonus, advice.ComboBonus > 0);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void APaperCompletingMysticIsValuedByItsCellsOnly(bool rule, int steps)
    {
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(3, 1, 3),
            ComboCounts = new Dictionary<string, int> { ["MYSTIC"] = 3 },
            Combos = MysticCombo,
        };
        if (rule) problem.ComboEngraving = Mystic(new GridPos(0, 0));
        var paper = new CharmSlot { InstanceId = 2, Definition = new CharmDefinition { MaxLevel = 5, Behavior = "Charm_WhitePaper" } };
        var neighbors = new Dictionary<GridPos, CharmSlot>
        {
            [new GridPos(0, 0)] = new CharmSlot { InstanceId = 1, Definition = new CharmDefinition { MaxLevel = 5, Categories = { "MYSTIC" } } },
            [new GridPos(1, 0)] = paper,
            [new GridPos(2, 0)] = new CharmSlot { InstanceId = 3, Definition = new CharmDefinition { MaxLevel = 5, Categories = { "MYSTIC" } } },
        };
        foreach (var pair in neighbors)
        {
            problem.Charms.Add(pair.Value);
            problem.CurrentCharms[pair.Value.InstanceId] = pair.Key;
        }

        Assert.Equal(steps * problem.Scale.ComboThreshold,
            PositionalWorth.ComboWorth(problem, paper, new GridPos(1, 0), neighbors), 9);
    }

    /// <summary>
    /// 버리기 조언은 "콤보 효과는 점수에 없다" 며 단계가 바뀌는 제거를 막는다. 신비는 사라지는 ×2
    /// 칸까지 점수가 셌으므로 막을 이유가 없다 - 여기서는 그 칸에 레벨이 없어 잃는 것이 없다.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void DroppingAHarmfulMysticIsAdvisedWhenTheStageIsScored(bool rule, bool advised)
    {
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(3, 1, 3),
            ComboCounts = new Dictionary<string, int> { ["MYSTIC"] = 3 },
            Combos = id => id == "MYSTIC" ? new ComboDefinition { Id = "MYSTIC", Thresholds = { 3 } } : null,
        };
        for (var id = 1; id <= 3; id++)
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Definition = new CharmDefinition { Id = "m" + id, EntityId = id, MaxLevel = 0, Categories = { "MYSTIC" } },
                Worth = new CharmWorth { Base = id == 3 ? -5 : 1, PerLevel = 0 },
            });
            problem.CurrentCharms[id] = new GridPos(id - 1, 0);
        }
        if (rule)
        {
            var mystic = new ComboEngravingRule { Category = "MYSTIC", Query = "O MUL/2", Positions = { new GridPos(0, 0) } };
            mystic.Tiers.Add(new ComboEngravingTier { Threshold = 3, Count = 1 });
            problem.ComboEngraving = mystic;
        }

        var advice = DiscardAdvisor.Rank(problem, PlacementSolver.Solve(problem));

        Assert.Equal(advised, advice.Any(entry => entry.InstanceId == 3));
    }

    private static int CountAt(PlacementProblem problem, IReadOnlyDictionary<int, GridPos> positions) =>
        ComboCounting.CountAll(problem.Charms.Where(charm => positions.ContainsKey(charm.InstanceId))
            .ToDictionary(charm => positions[charm.InstanceId], charm => charm)).GetValueOrDefault("MYSTIC");

    /// <summary>
    /// 신비와 하얀 종이를 섞은 무작위 판. 종이가 신비 사이에 서느냐로 문턱을 넘나들게 하고,
    /// 값진 아티팩트를 섞어 각인 칸이 탐나게 한다.
    /// </summary>
    private static PlacementProblem MysticBoard(int seed)
    {
        var random = new Random(seed * 7919 + 3);
        var width = 4 + random.Next(2);
        var height = 2 + random.Next(2);
        var storage = width * height - random.Next(2);
        var problem = new PlacementProblem { Grid = new GridSpec(width, height, storage) };

        if (random.Next(2) == 0)
            problem.Tablets.Add(new TabletSlot
            {
                InstanceId = 900,
                Definition = new TabletDefinition { Id = "t", EntityId = 700, Query = "RIGHT 1", ConditionQuery = "O CHARM" },
            });

        var charms = storage - problem.Tablets.Count - random.Next(2);
        for (var index = 0; index < charms; index++)
        {
            var definition = new CharmDefinition { Id = "c" + index, EntityId = 200 + index, MaxLevel = 5 };
            var kind = index < 3 ? index : random.Next(8);
            if (kind <= 1 || kind == 3) definition.Categories.Add("MYSTIC");
            else if (kind == 2) definition.Behavior = "Charm_WhitePaper";
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = index,
                Enchant = random.Next(0, 2),
                Definition = definition,
                Worth = new CharmWorth { ByLevel = Enumerable.Range(0, 6).Select(level => (double)level * random.Next(1, 6)).ToArray() },
            });
        }

        var cells = Enumerable.Range(0, storage).Select(problem.Grid.ToPosition).OrderBy(_ => random.Next()).ToList();
        var at = 0;
        foreach (var tablet in problem.Tablets)
            problem.CurrentTablets[tablet.InstanceId] = new TabletSpot(cells[at++], 0);
        foreach (var charm in problem.Charms)
            if (at < cells.Count) problem.CurrentCharms[charm.InstanceId] = cells[at++];

        problem.ComboCounts = new Dictionary<string, int> { ["MYSTIC"] = CountAt(problem, problem.CurrentCharms) };
        var positions = Enumerable.Range(0, storage).Select(problem.Grid.ToPosition).OrderBy(_ => random.Next()).Take(3);
        var rule = new ComboEngravingRule { Category = "MYSTIC", Query = "O MUL/2" };
        rule.Tiers.Add(new ComboEngravingTier { Threshold = 3, Count = 1 });
        rule.Tiers.Add(new ComboEngravingTier { Threshold = 4, Count = 2 });
        rule.Positions.AddRange(positions);
        problem.ComboEngraving = rule;
        return problem;
    }
}
