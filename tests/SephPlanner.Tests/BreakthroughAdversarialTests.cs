using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 돌파의 나침반 몫 나누기(<c>Compass.Lifted</c>, <c>PlacementSolver.LiftWorth</c>)와 끼워 넣기 다듬기
/// (<c>PolishLiftInsertions</c>)를 일부러 어긋난 판으로 흔든다. 기대값은 docs/RESEARCH.md 의 1.1.0 나침반
/// 규칙이다 - 돌파는 제 몫이 없고, 길에서 침에 가장 가까운 돌파가 희귀도 조건을 풀어 준 덤을 갖는다.
/// 같은 무작위 판으로 다듬기가 끝난 배치에 한 수로 나아지는 길이 남지 않는지도 본다.
/// </summary>
public class BreakthroughAdversarialTests
{
    private static readonly GridPos Up = new(0, -1);

    private static CharmSlot Root(int id, Rarity rarity = Rarity.Common, double worth = 0)
    {
        var root = new CharmSlot
        {
            InstanceId = id,
            Definition = new CharmDefinition
            {
                EntityId = id,
                IsAttackable = true,
                MaxLevel = 4,
                Rarity = rarity,
                Categories = { "EMBER" },
            },
        };
        if (worth > 0) root.Worth = new CharmWorth { Base = worth, PerLevel = worth };
        return root;
    }

    private static CharmSlot Needle(int id, bool rarityCondition = true, params double[] extra) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            EntityId = id,
            MaxLevel = 4,
            Behavior = "Charm_CompassDamageUp",
            DependencyParents = { Up },
            HasDependencyCondition = rarityCondition,
            DependencyMaxRarity = Rarity.Uncommon,
            DependencyBonusByLevel = { 10, 10, 10, 10, 10 },
            DependencyExtraByLevel = { extra.Length > 0 ? extra[0] : 25, extra.Length > 1 ? extra[1] : 25,
                extra.Length > 2 ? extra[2] : 25, extra.Length > 3 ? extra[3] : 25, extra.Length > 4 ? extra[4] : 25 },
        },
    };

    private static CharmSlot Distribute(int id) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            EntityId = id,
            MaxLevel = 4,
            Behavior = "Charm_Compass",
            DependencyParents = { new(-1, 0), new(1, 0) },
        },
    };

    private static CharmSlot Breakthrough(int id) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            EntityId = id,
            MaxLevel = 4,
            Rarity = Rarity.Legend,
            Behavior = "Charm_CompassBreakthrough",
            DependencyParents = { Up },
        },
    };

    private static CharmSlot Filler(int id) => new()
    {
        InstanceId = id,
        IsFiller = true,
        Definition = new CharmDefinition { EntityId = id, MaxLevel = 4 },
    };

    private static PlacementProblem Problem(GridSpec grid, params (CharmSlot Charm, GridPos Cell)[] placed)
    {
        var problem = new PlacementProblem { Grid = grid };
        foreach (var (charm, cell) in placed)
        {
            problem.Charms.Add(charm);
            problem.CurrentCharms[charm.InstanceId] = cell;
        }
        return problem;
    }

    private static Arrangement Score(PlacementProblem problem, IReadOnlyDictionary<int, GridPos>? positions = null) =>
        PlacementSolver.Score(problem, new List<TabletPlacement>(), positions ?? problem.CurrentCharms);

    private static Dictionary<GridPos, CharmSlot> ByCell(PlacementProblem problem) =>
        problem.Charms.ToDictionary(charm => problem.CurrentCharms[charm.InstanceId]);

    private static double W(PlacementProblem problem, int id, int level = 0) =>
        problem.Charms.Single(charm => charm.InstanceId == id).Worth.At(level);

    private static void AssertValid(PlacementProblem problem, Arrangement solved)
    {
        Assert.Equal(problem.Charms.Count, solved.CharmPositions.Count);
        foreach (var charm in problem.Charms)
        {
            Assert.True(solved.CharmPositions.ContainsKey(charm.InstanceId), $"{charm.InstanceId} 가 빠졌다");
            Assert.True(problem.Grid.Contains(solved.CharmPositions[charm.InstanceId]),
                $"{charm.InstanceId} 가 열린 칸 밖 {solved.CharmPositions[charm.InstanceId]} 에 있다");
            if (charm.Immovable)
                Assert.Equal(problem.CurrentCharms[charm.InstanceId], solved.CharmPositions[charm.InstanceId]);
        }
        Assert.Equal(solved.CharmPositions.Count, solved.CharmPositions.Values.Distinct().Count());
        Assert.Equal(Score(problem, solved.CharmPositions).Score, solved.Score, 6);
    }

    private static string Layout(Arrangement solved) =>
        string.Join(" ", solved.CharmPositions.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}@{pair.Value}"));

    // ---- 채점: 몫 나누기의 합이 게임과 같은가 ----

    [Fact]
    public void TwoStackedBreakthroughsLiftOnlyOnce()
    {
        var problem = Problem(new GridSpec(1, 4, 4),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (Breakthrough(3), new GridPos(0, 2)),
            (Needle(4), new GridPos(0, 3)));

        Assert.Equal(W(problem, 1) + W(problem, 4) * (1 + 25.0 / 10), Score(problem).Score, 6);

        // 침에 가장 가까운 돌파가 덤을 갖는다.
        var lifted = new List<LiftedRoot>();
        Compass.Lifted(problem.Charms[2], new GridPos(0, 2), ByCell(problem), default, lifted);
        Assert.Single(lifted);
        Compass.Lifted(problem.Charms[1], new GridPos(0, 1), ByCell(problem), default, lifted);
        Assert.Empty(lifted);
    }

    /// <summary>분배 아래 침은 두 뿌리에 몫을 준다. 돌파를 지나는 쪽 뿌리만 덤을 받는다.</summary>
    [Fact]
    public void ABreakthroughOnOneBranchOfADistributionLiftsOnlyThatRoot()
    {
        var problem = Problem(new GridSpec(3, 3, 9),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (Distribute(3), new GridPos(1, 1)),
            (Root(4, Rarity.Rare), new GridPos(2, 1)),
            (Needle(5), new GridPos(1, 2)));

        var expected = W(problem, 1) + W(problem, 4) + W(problem, 3) + W(problem, 5) * ((1 + 25.0 / 10) + 1);
        Assert.Equal(expected, Score(problem).Score, 6);
    }

    /// <summary>돌파가 분배 위에 있으면 분배 아래 침의 길이 그 돌파를 지난다.</summary>
    [Fact]
    public void ABreakthroughAboveADistributionLiftsTheNeedleBelowIt()
    {
        var problem = Problem(new GridSpec(3, 3, 9),
            (Root(1, Rarity.Rare), new GridPos(1, 0)),
            (Breakthrough(2), new GridPos(1, 1)),
            (Distribute(3), new GridPos(2, 1)),
            (Needle(4), new GridPos(2, 2)));
        // 분배의 왼쪽 부모가 돌파, 돌파의 위가 뿌리. 오른쪽 부모 칸은 비었다.

        Assert.Equal(W(problem, 1) + W(problem, 3) + W(problem, 4) * (1 + 25.0 / 10), Score(problem).Score, 6);
    }

    [Fact]
    public void ABreakthroughAddsNothingForAnUncommonTarget()
    {
        var lifted = Problem(new GridSpec(1, 3, 3),
            (Root(1, Rarity.Uncommon), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (Needle(3), new GridPos(0, 2)));
        var plain = Problem(new GridSpec(1, 3, 3),
            (Root(1, Rarity.Uncommon), new GridPos(0, 0)),
            (Needle(3), new GridPos(0, 1)),
            (Breakthrough(2), new GridPos(0, 2)));

        Assert.Equal(W(lifted, 1) + W(lifted, 3) * (1 + 25.0 / 10), Score(lifted).Score, 6);
        Assert.Equal(Score(plain).Score, Score(lifted).Score, 6);
    }

    [Fact]
    public void ABreakthroughAddsNothingForANeedleWithoutTheRarityCondition()
    {
        var problem = Problem(new GridSpec(1, 3, 3),
            (Root(1, Rarity.Legend), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (Needle(3, rarityCondition: false), new GridPos(0, 2)));

        Assert.Equal(W(problem, 1) + W(problem, 3), Score(problem).Score, 6);
    }

    [Fact]
    public void ABreakthroughBelowTheNeedleIsWorthNothing()
    {
        var problem = Problem(new GridSpec(1, 3, 3),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Needle(2), new GridPos(0, 1)),
            (Breakthrough(3), new GridPos(0, 2)));

        Assert.Equal(W(problem, 1) + W(problem, 2), Score(problem).Score, 6);
    }

    /// <summary>돌파는 그 아래 침 전부에게 조건을 풀어 준다. 위에 있는 침은 받지 못한다.</summary>
    [Fact]
    public void ABreakthroughLiftsOnlyTheNeedlesBelowIt()
    {
        var below = Problem(new GridSpec(1, 4, 4),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (Needle(3), new GridPos(0, 2)),
            (Needle(4), new GridPos(0, 3)));
        var mixed = Problem(new GridSpec(1, 4, 4),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Needle(3), new GridPos(0, 1)),
            (Breakthrough(2), new GridPos(0, 2)),
            (Needle(4), new GridPos(0, 3)));

        var lift = 1 + 25.0 / 10;
        Assert.Equal(W(below, 1) + W(below, 3) * lift + W(below, 4) * lift, Score(below).Score, 6);
        Assert.Equal(W(mixed, 1) + W(mixed, 3) + W(mixed, 4) * lift, Score(mixed).Score, 6);
    }

    [Fact]
    public void ADisabledBreakthroughCutsTheChain()
    {
        var problem = Problem(new GridSpec(1, 3, 3),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (Needle(3), new GridPos(0, 2)));
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 1), Disable = 1 });

        Assert.Equal(W(problem, 1), Score(problem).Score, 6);
    }

    /// <summary>돌파의 몫은 침 자신과 같은 레벨로 잰다. 레벨마다 덤이 다른 표로 어긋나면 드러난다.</summary>
    [Fact]
    public void TheLiftUsesTheNeedlesOwnLevel()
    {
        var needle = Needle(3, true, 25, 30, 35, 40, 45);
        needle.Enchant = 2;
        var problem = Problem(new GridSpec(1, 3, 3),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (needle, new GridPos(0, 2)));

        var scored = Score(problem);
        var level = scored.EffectiveLevels[new GridPos(0, 2)];
        var rootLevel = scored.EffectiveLevels[new GridPos(0, 0)];
        Assert.Equal(2, level);
        Assert.Equal(W(problem, 1, rootLevel) + W(problem, 3, level) * (1 + 35.0 / 10), scored.Score, 6);
    }

    // ---- 배정의 맞바꾸기 갈음: 돌파를 다른 칸에 세워 볼 때 ----

    [Fact]
    public void LiftedReadsTheSwapWhenTheBreakthroughTriesACellOnThePath()
    {
        var problem = Problem(new GridSpec(2, 3, 6),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Filler(2), new GridPos(0, 1)),
            (Needle(3), new GridPos(0, 2)),
            (Breakthrough(4), new GridPos(1, 2)));
        var neighbors = ByCell(problem);
        var lifted = new List<LiftedRoot>();

        // 채움이 있던 칸에 세우면 채움은 돌파의 옛 칸으로 가고, 침의 길이 돌파를 지난다.
        Compass.Lifted(problem.Charms[3], new GridPos(0, 1), neighbors, default, lifted);
        Assert.Single(lifted);
        Assert.Equal(3, lifted[0].Needle.InstanceId);
        Assert.Equal(1, lifted[0].Root.InstanceId);

        // 침의 칸에 세우면 침이 돌파의 옛 칸(1,2)으로 가고 그 위는 비었다.
        Compass.Lifted(problem.Charms[3], new GridPos(0, 2), neighbors, default, lifted);
        Assert.Empty(lifted);
    }

    [Fact]
    public void LiftedTreatsTheBreakthroughsOldCellAsSwapped()
    {
        var problem = Problem(new GridSpec(2, 3, 6),
            (Root(1, Rarity.Rare), new GridPos(0, 0)),
            (Breakthrough(2), new GridPos(0, 1)),
            (Needle(3), new GridPos(0, 2)));
        var lifted = new List<LiftedRoot>();

        // 빈 칸으로 옮겨 보면 옛 칸은 비어 침의 길이 끊긴다.
        Compass.Lifted(problem.Charms[1], new GridPos(1, 1), ByCell(problem), default, lifted);
        Assert.Empty(lifted);
        Compass.Lifted(problem.Charms[1], new GridPos(0, 1), ByCell(problem), default, lifted);
        Assert.Single(lifted);
    }

    // ---- 끼워 넣기: 판이 막힌 곳에서 ----

    private static CharmSlot Target(int id, double worth = 5)
    {
        var target = Root(id, Rarity.Legend, worth);
        target.IsSupportTarget = true;
        return target;
    }

    private static CharmSlot Kunai(int id, double worth = 10) => Root(id, Rarity.Uncommon, worth);

    private static CharmSlot WorthNeedle(int id)
    {
        var needle = Needle(id);
        needle.Worth = new CharmWorth { Base = 2, PerLevel = 2 };
        return needle;
    }

    /// <summary>침 아래 칸에 못 박힌 아이템이 있으면 사슬을 내릴 수 없다. 못 박힌 것은 그대로다.</summary>
    [Fact]
    public void AnInsertionNeverPushesOntoAPinnedCell()
    {
        var pinned = Root(9, Rarity.Common, 1);
        pinned.Immovable = true;
        var problem = Problem(new GridSpec(2, 3, 6),
            (Target(1), new GridPos(0, 0)),
            (Kunai(2), new GridPos(1, 0)),
            (WorthNeedle(3), new GridPos(0, 1)),
            (Breakthrough(4), new GridPos(1, 1)),
            (pinned, new GridPos(0, 2)));

        var solved = PlacementSolver.Solve(problem);

        AssertValid(problem, solved);
        Assert.Equal(1, solved.SupportTargetMatches);
    }

    /// <summary>뿌리가 못 박혀 있어도 끼워 넣기는 뿌리를 옮기지 않으므로 된다.</summary>
    [Fact]
    public void AnInsertionWorksUnderAPinnedTarget()
    {
        var target = Target(1);
        target.Immovable = true;
        var problem = Problem(new GridSpec(2, 3, 6),
            (target, new GridPos(0, 0)),
            (Kunai(2), new GridPos(1, 0)),
            (WorthNeedle(3), new GridPos(0, 1)),
            (Breakthrough(4), new GridPos(1, 1)));

        var solved = PlacementSolver.Solve(problem);

        AssertValid(problem, solved);
        Assert.Equal(new GridPos(0, 1), solved.CharmPositions[4]);
        Assert.Equal(new GridPos(0, 2), solved.CharmPositions[3]);
    }

    /// <summary>사슬을 내릴 칸이 잠겨 있다. 잠긴 칸에는 아무것도 가지 않는다.</summary>
    [Fact]
    public void AnInsertionNeverPushesIntoALockedCell()
    {
        var target = Target(1);
        target.Immovable = true;
        var kunai = Kunai(2);
        kunai.Immovable = true;
        // 2x3 에 다섯 칸만 열려 (1,2) 가 잠겼다.
        var problem = Problem(new GridSpec(2, 3, 5),
            (kunai, new GridPos(0, 0)),
            (target, new GridPos(1, 0)),
            (WorthNeedle(3), new GridPos(1, 1)),
            (Breakthrough(4), new GridPos(0, 1)));

        var solved = PlacementSolver.Solve(problem);

        AssertValid(problem, solved);
        Assert.Equal(new GridPos(1, 1), solved.CharmPositions[3]);
    }

    /// <summary>침 아래 매달린 침도 함께 한 칸 내려간다.</summary>
    [Fact]
    public void AnInsertionShiftsTheWholeHangingChain()
    {
        var target = Target(1);
        target.Immovable = true;
        var kunai = Kunai(2);
        kunai.Immovable = true;
        var problem = Problem(new GridSpec(2, 4, 8),
            (target, new GridPos(0, 0)),
            (kunai, new GridPos(1, 0)),
            (WorthNeedle(3), new GridPos(0, 1)),
            (WorthNeedle(5), new GridPos(0, 2)),
            (Breakthrough(4), new GridPos(1, 1)));

        var solved = PlacementSolver.Solve(problem);

        AssertValid(problem, solved);
        Assert.Equal(new GridPos(0, 1), solved.CharmPositions[4]);
        // 두 침은 똑같아 순서는 가리지 않는다.
        Assert.Equal(new[] { new GridPos(0, 2), new GridPos(0, 3) },
            new[] { solved.CharmPositions[3], solved.CharmPositions[5] }.OrderBy(cell => cell.Y));
    }

    private static PlacementProblem ChainBesideAPriorityComboRoot(Rarity targetRarity)
    {
        var target = Root(1, targetRarity, 5);
        target.IsSupportTarget = true;
        target.Definition.Categories.Clear();
        target.Definition.Categories.Add("SHADOW");
        var kunai = Kunai(2);
        kunai.Definition.Categories.Clear();
        kunai.Definition.Categories.Add("PRECISION");
        var problem = Problem(new GridSpec(2, 4, 8),
            (target, new GridPos(0, 0)),
            (kunai, new GridPos(1, 0)),
            (Breakthrough(4), new GridPos(0, 1)),
            (WorthNeedle(3), new GridPos(0, 2)));
        problem.PriorityCategories.Add("PRECISION");
        problem.ComboCounts = new Dictionary<string, int> { ["PRECISION"] = 1, ["SHADOW"] = 1 };
        problem.Combos = id => id == "PRECISION" ? new ComboDefinition { Id = id, Thresholds = { 2 } } : null;
        return problem;
    }

    /// <summary>
    /// 제보 eeb4f2ad. 돌파가 지정 대상과 침 사이에 있는데, 쿠나이 밑으로 가면 우선 콤보(정밀)가 하나 찬다.
    /// 지정 대상이 콤보보다 앞서므로 돌파는 사슬에 남는다 - 예전에는 돌파를 지원으로 세지 않아 콤보가 빼 갔다.
    /// </summary>
    [Fact]
    public void APriorityComboDoesNotPullTheBreakthroughOffTheDesignatedTarget()
    {
        var problem = ChainBesideAPriorityComboRoot(Rarity.Rare);

        var solved = PlacementSolver.Solve(problem);

        AssertValid(problem, solved);
        var target = solved.CharmPositions[1];
        Assert.Equal(target.Offset(0, 1), solved.CharmPositions[4]);
        Assert.Equal(target.Offset(0, 2), solved.CharmPositions[3]);
        Assert.Equal(2, solved.SupportTargetMatches);
    }

    /// <summary>희귀도 조건이 이미 맞는 대상에게는 돌파가 풀어 줄 것이 없다. 지원으로 세지 않으니 콤보가 데려간다.</summary>
    [Fact]
    public void ABreakthroughThatLiftsNothingIsNotCountedForTheDesignatedTarget()
    {
        var problem = ChainBesideAPriorityComboRoot(Rarity.Uncommon);

        Assert.Equal(1, Score(problem).SupportTargetMatches);

        var solved = PlacementSolver.Solve(problem);

        AssertValid(problem, solved);
        Assert.Equal(solved.CharmPositions[2].Offset(0, 1), solved.CharmPositions[4]);
        Assert.Equal(solved.CharmPositions[1].Offset(0, 1), solved.CharmPositions[3]);
    }

    /// <summary>
    /// 침이 대상 바로 밑 맨 아랫줄에 있어 사슬을 내릴 수 없다. 대상째 한 칸 올리면 돌파가 들어간다 -
    /// 788aa22 는 이 판에서 돌파를 대상 밑에 넣었다(게임 값 12.5 대 10).
    /// </summary>
    [Fact]
    public void ABreakthroughStillReachesANeedleOnTheBottomRow()
    {
        var problem = Problem(new GridSpec(4, 4, 16),
            (Root(1, Rarity.Rare, 1), new GridPos(1, 0)),
            (Root(2, Rarity.Legend, 8), new GridPos(0, 2)),
            (Needle(3), new GridPos(3, 3)),
            (Breakthrough(4), new GridPos(3, 0)));
        var ideal = new Dictionary<int, GridPos>
        {
            [1] = new(1, 0),
            [2] = new(3, 1),
            [4] = new(3, 2),
            [3] = new(3, 3),
        };

        var solved = PlacementSolver.Solve(problem);

        AssertValid(problem, solved);
        Assert.True(solved.Score >= Score(problem, ideal).Score - 1e-6,
            $"점수 {solved.Score} < 돌파를 끼운 배치 {Score(problem, ideal).Score}: {Layout(solved)}");
    }

    [Fact]
    public void ABreakthroughStillReachesANeedleOnTheBottomRowOfASmallBag()
    {
        var problem = Problem(new GridSpec(2, 3, 6),
            (Root(1, Rarity.Rare, 9), new GridPos(1, 1)),
            (Root(2, Rarity.Uncommon, 2), new GridPos(0, 0)),
            (Needle(5), new GridPos(1, 2)),
            (Breakthrough(4), new GridPos(0, 2)));
        var ideal = new Dictionary<int, GridPos>
        {
            [1] = new(1, 0),
            [4] = new(1, 1),
            [5] = new(1, 2),
            [2] = new(0, 0),
        };

        var solved = PlacementSolver.Solve(problem);

        AssertValid(problem, solved);
        Assert.True(solved.Score >= Score(problem, ideal).Score - 1e-6,
            $"점수 {solved.Score} < 돌파를 끼운 배치 {Score(problem, ideal).Score}: {Layout(solved)}");
    }

    // ---- 무작위 판: 배치가 늘 유효하고, 같은 입력에 같은 답 ----

    private static PlacementProblem RandomBoard(int seed)
    {
        var random = new Random(seed);
        var width = random.Next(2, 5);
        var height = random.Next(3, 5);
        var problem = new PlacementProblem { Grid = new GridSpec(width, height, width * height - random.Next(0, 3)) };
        var open = new List<GridPos>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (problem.Grid.Contains(x, y)) open.Add(new GridPos(x, y));
        var count = Math.Min(open.Count - random.Next(0, 3), random.Next(4, 9));
        var cells = open.OrderBy(_ => random.Next()).Take(count).ToList();
        var rarities = new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Legend };
        for (var index = 0; index < count; index++)
        {
            var id = index + 1;
            var roll = random.Next(10);
            CharmSlot charm = index < 2 || roll < 3 ? Root(id, rarities[random.Next(4)], random.Next(1, 12))
                : roll < 6 ? Needle(id, random.Next(3) > 0)
                : roll < 7 ? Distribute(id)
                : roll < 9 ? Breakthrough(id)
                : Filler(id);
            charm.IsSupportTarget = charm.Definition.IsAttackable && random.Next(6) == 0;
            problem.Charms.Add(charm);
            problem.CurrentCharms[id] = cells[index];
        }
        if (random.Next(4) == 0) problem.Charms[random.Next(count)].Immovable = true;
        return problem;
    }

    [Fact]
    public void RandomBoardsWithBreakthroughsStayValidAndIncrementalScoringAgrees()
    {
        for (var seed = 0; seed < 120; seed++)
        {
            var problem = RandomBoard(seed);
            var solved = PlacementSolver.Solve(problem, new SolverOptions { VerifyIncrementalPolish = true });
            AssertValid(problem, solved);
        }
    }

    [Fact]
    public void TheSameBoardSolvesTheSameWay()
    {
        for (var seed = 0; seed < 40; seed++)
        {
            var first = PlacementSolver.Solve(RandomBoard(seed));
            var second = PlacementSolver.Solve(RandomBoard(seed));
            Assert.Equal(first.CharmPositions.OrderBy(p => p.Key), second.CharmPositions.OrderBy(p => p.Key));
            Assert.Equal(first.Score, second.Score);
        }
    }
    // ---- 다듬기가 끝난 배치에는 한 수로 나아지는 길이 없다 ----

    /// <summary>
    /// 다듬기 전 배정과 다듬은 배치가 같은 값이면 다듬기 전 것이 남았는데, 다듬기는 그 배정이 아니라 처음부터
    /// 다시 출발해 다른 지역 최적에 닿았다. 남은 배정은 비어 있는 지금 칸으로 한 칸만 돌아가도 자리 유지 몫이 늘었다.
    /// </summary>
    [Fact]
    public void ASolvedBoardKeepsWhatASingleMoveWouldKeep()
    {
        var problem = Problem(new GridSpec(3, 3, 8),
            (Root(1, Rarity.Legend, 6), new GridPos(0, 2)),
            (Root(2, Rarity.Legend, 10), new GridPos(2, 1)),
            (Needle(3, rarityCondition: false), new GridPos(1, 1)),
            (Needle(4), new GridPos(0, 1)));

        var solved = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(0, 2), solved.CharmPositions[1]);
        Assert.Null(BetterSingleMove(problem, solved));
    }

    [Fact]
    public void NoSingleMoveImprovesARandomSolvedBoard()
    {
        for (var variant = 0; variant < 2; variant++)
            for (var seed = 0; seed < 300; seed++)
            {
                var problem = variant == 0 ? RandomBoard(seed) : LargerRandomBoard(seed);
                var solved = PlacementSolver.Solve(problem);
                var move = BetterSingleMove(problem, solved);
                Assert.True(move is null, $"판 {variant}/{seed}: {Layout(solved)} 에서 {move} 가 낫다");
            }
    }

    /// <summary>못 박히지 않은 아이템 하나를 다른 칸으로 옮기거나(차 있으면 맞바꾸기) 솔버의 비교 기준으로 나아지는 수.</summary>
    private static string? BetterSingleMove(PlacementProblem problem, Arrangement solved)
    {
        var open = new List<GridPos>();
        for (var y = 0; y < problem.Grid.Height; y++)
            for (var x = 0; x < problem.Grid.Width; x++)
                if (problem.Grid.Contains(x, y)) open.Add(new GridPos(x, y));
        foreach (var charm in problem.Charms.Where(charm => !charm.Immovable))
            foreach (var cell in open)
            {
                var positions = new Dictionary<int, GridPos>(solved.CharmPositions);
                var from = positions[charm.InstanceId];
                if (from == cell) continue;
                var occupant = problem.Charms.FirstOrDefault(other => positions[other.InstanceId] == cell);
                if (occupant is not null && occupant.Immovable) continue;
                positions[charm.InstanceId] = cell;
                if (occupant is not null) positions[occupant.InstanceId] = from;
                if (PriorityPlacement.Compare(Score(problem, positions), solved) > 0) return $"{charm.InstanceId}->{cell}";
            }
        return null;
    }

    private static PlacementProblem LargerRandomBoard(int seed)
    {
        var random = new Random(seed * 7919 + 1);
        var width = random.Next(3, 6);
        var height = random.Next(3, 6);
        var problem = new PlacementProblem { Grid = new GridSpec(width, height, width * height - random.Next(0, 3)) };
        var open = new List<GridPos>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (problem.Grid.Contains(x, y)) open.Add(new GridPos(x, y));
        var count = Math.Min(open.Count - random.Next(0, 3), random.Next(6, 11));
        var cells = open.OrderBy(_ => random.Next()).Take(count).ToList();
        var rarities = new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Legend };
        for (var index = 0; index < count; index++)
        {
            var id = index + 1;
            var roll = random.Next(10);
            CharmSlot charm = index < 2 || roll < 4 ? Root(id, rarities[random.Next(4)], random.Next(1, 12))
                : roll < 7 ? Needle(id, random.Next(3) > 0)
                : roll < 8 ? Distribute(id)
                : roll < 9 ? Breakthrough(id)
                : Filler(id);
            charm.IsSupportTarget = charm.Definition.IsAttackable && random.Next(6) == 0;
            problem.Charms.Add(charm);
            problem.CurrentCharms[id] = cells[index];
        }
        if (random.Next(4) == 0) problem.Charms[random.Next(count)].Immovable = true;
        return problem;
    }
}
