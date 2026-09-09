using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 자리에 따라 카테고리가 달라지는 아티팩트는 콤보 개수를 자기 자리로 바꾼다. 게임이 센 개수에
/// 그 몫이 이미 들어 있는 것을 빼지 않으면, 옮길 때마다 개수가 바뀌고 다음 계산이 도로 옮기라고
/// 하는 2주기가 생긴다 - 실제 판에서 F8 을 누를 때마다 6~7수가 나왔다.
/// </summary>
public class ComboCountingTests
{
    private static CharmDefinition Attackable(string id, string category) => new()
    {
        Id = id,
        MaxLevel = 5,
        Categories = { category },
        IsAttackable = true,
    };

    private static CharmDefinition Needle(string id) => new()
    {
        Id = id,
        MaxLevel = 5,
        DependencyOffsetY = -1,
        DependencyBonusByLevel = { 5, 10, 15 },
    };

    private static CharmSlot Slot(int id, CharmDefinition definition) => new() { InstanceId = id, Definition = definition };

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NeedleUsesInstanceAttackabilityWhenAvailable(bool? attackable, bool connected)
    {
        var needle = Slot(1, Needle("N"));
        var target = Slot(2, Attackable("M", "EMBER"));
        target.Definition.IsMagic = true;
        target.IsAttackable = attackable;
        var neighbors = new Dictionary<GridPos, CharmSlot>
        {
            [new GridPos(0, 1)] = needle,
            [new GridPos(0, 0)] = target,
        };
        var inherited = new List<string>();
        ComboCounting.PositionalCategories(needle, new GridPos(0, 1), neighbors, inherited);
        Assert.Equal(connected ? 1 : 0, inherited.Count);
        Assert.Equal(connected ? 1 : 0, PositionalWorth.DependencyFactor(needle, new GridPos(0, 1), 0, neighbors));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PaperReadsTheCategoryInheritedByAnAdjacentNeedle(bool chain, bool mirror)
    {
        var paper = Slot(4, new CharmDefinition { Behavior = "Charm_WhitePaper" });
        var byCell = new Dictionary<GridPos, CharmSlot>
        {
            [new GridPos(0, 1)] = Slot(1, Attackable("A", "EMBER")),
            [new GridPos(0, 2)] = Slot(2, Needle("N")),
            [new GridPos(1, 2)] = paper,
            [new GridPos(2, 2)] = Slot(3, Attackable("B", "EMBER")),
        };
        if (chain)
        {
            byCell[new GridPos(0, 1)] = Slot(5, Needle("N2"));
            byCell[new GridPos(0, 0)] = Slot(1, Attackable("A", "EMBER"));
        }
        if (mirror)
            byCell = byCell.ToDictionary(pair => new GridPos(2 - pair.Key.X, pair.Key.Y), pair => pair.Value);

        var categories = new List<string>();
        ComboCounting.PositionalCategories(paper, new GridPos(1, 2), byCell, categories);
        Assert.Equal(new[] { "EMBER" }, categories);
        Assert.Equal(chain ? 5 : 4, ComboCounting.CountAll(byCell)["EMBER"]);

        var counts = ComboCounting.CountAll(byCell);
        var problem = new PlacementProblem { ComboCounts = counts };
        foreach (var pair in byCell)
        {
            problem.Charms.Add(pair.Value);
            problem.CurrentCharms[pair.Value.InstanceId] = pair.Key;
        }
        Assert.Equal(counts["EMBER"] - 1, ComboCounting.CountFor(problem, paper, "EMBER", byCell));

        byCell.Remove(new GridPos(mirror ? 2 : 0, chain ? 0 : 1));
        categories.Clear();
        ComboCounting.PositionalCategories(paper, new GridPos(1, 2), byCell, categories);
        Assert.Empty(categories);
    }

    [Fact]
    public void TheGameCountIncludesWhatANeedleInherits()
    {
        // 침 위에 침, 그 위에 EMBER 아티팩트. 게임은 사슬의 침 둘 다 EMBER 로 센다(실제 판에서
        // FLAMESWORD 9종 + 침 둘 = 11 로 확인).
        var byCell = new Dictionary<GridPos, CharmSlot>
        {
            [new GridPos(0, 0)] = Slot(1, Attackable("A", "EMBER")),
            [new GridPos(0, 1)] = Slot(2, Needle("N1")),
            [new GridPos(0, 2)] = Slot(3, Needle("N2")),
            [new GridPos(3, 0)] = Slot(4, Needle("N3")),
        };

        var counts = ComboCounting.CountAll(byCell);

        Assert.Equal(3, counts["EMBER"]);
        Assert.Single(counts);
    }

    [Fact]
    public void AKeyCountsOnlyTheCategoryOfItsRow()
    {
        var key = new CharmDefinition
        {
            Id = "KEY",
            MaxLevel = 5,
            Categories = { "IGNORED" },
            LineCategories = { "STURDY", "FROST", "LAKE" },
        };
        var byCell = new Dictionary<GridPos, CharmSlot> { [new GridPos(2, 4)] = Slot(1, key) };

        var counts = ComboCounting.CountAll(byCell);

        Assert.Equal(1, counts["FROST"]);
        Assert.False(counts.ContainsKey("IGNORED"));
    }

    /// <summary>
    /// EMBER 아티팩트 밑의 침이 EMBER 를 2 로 만들어 임계값 2 를 채우고 있고, FROST 는 1 이다.
    /// 게임이 센 개수는 그 상태다.
    /// </summary>
    private static PlacementProblem TwoCombos(int emberThreshold, int frostThreshold)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 2, 12) };
        problem.Charms.Add(Slot(1, Attackable("E", "EMBER")));
        problem.Charms.Add(Slot(2, Attackable("F", "FROST")));
        problem.Charms.Add(Slot(3, Needle("N")));
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.CurrentCharms[2] = new GridPos(3, 0);
        problem.CurrentCharms[3] = new GridPos(0, 1);
        problem.ComboCounts = new Dictionary<string, int> { ["EMBER"] = 2, ["FROST"] = 1 };
        problem.Combos = id => id switch
        {
            "EMBER" => new ComboDefinition { Id = "EMBER", Thresholds = { emberThreshold } },
            "FROST" => new ComboDefinition { Id = "FROST", Thresholds = { frostThreshold } },
            _ => null,
        };
        return problem;
    }

    [Fact]
    public void TheNeedlesOwnShareIsNotCountedAgainstIt()
    {
        var problem = TwoCombos(emberThreshold: 2, frostThreshold: 3);
        var needle = problem.Charms[2];
        var byCell = new Dictionary<GridPos, CharmSlot>
        {
            [new GridPos(0, 0)] = problem.Charms[0],
            [new GridPos(3, 0)] = problem.Charms[1],
            [new GridPos(0, 1)] = needle,
        };

        // 게임 개수 2 에서 침 자신의 1 을 뺀 1 이 출발점이다. 그래야 "하나 더면 2" 가 맞는다.
        Assert.Equal(1, ComboCounting.CountFor(problem, needle, "EMBER", byCell));
        Assert.Equal(1, ComboCounting.CountFor(problem, needle, "FROST", byCell));
    }

    [Fact]
    public void ANeedleStaysUnderTheComboItAlreadyCompletes()
    {
        // 겹쳐 세면 EMBER 는 "이미 2 라 하나 더 모을 임계값이 없다"(0)가 되고 FROST 는 진행(0.43)이
        // 되어 침이 FROST 밑으로 간다. 그러면 게임이 EMBER 1 / FROST 2 로 다시 세고 다음 계산이
        // 침을 도로 부른다.
        var arrangement = PlacementSolver.Solve(TwoCombos(emberThreshold: 2, frostThreshold: 3));

        Assert.Equal(new GridPos(0, 1), arrangement.CharmPositions[3]);
    }

    [Fact]
    public void FollowingTheProposalAndRecountingLikeTheGameReachesAFixedPoint()
    {
        // F8 을 연달아 누르는 것과 같다 - 풀고, 옮기고, 게임처럼 다시 세고, 다시 푼다.
        var problem = TwoCombos(emberThreshold: 2, frostThreshold: 2);
        var first = PlacementSolver.Solve(problem);

        for (var round = 0; round < 3; round++)
        {
            var byCell = new Dictionary<GridPos, CharmSlot>();
            foreach (var charm in problem.Charms)
            {
                var cell = first.CharmPositions[charm.InstanceId];
                problem.CurrentCharms[charm.InstanceId] = cell;
                byCell[cell] = charm;
            }
            problem.ComboCounts = ComboCounting.CountAll(byCell);
            problem.BaseComboCounts = null;

            var next = PlacementSolver.Solve(problem);
            foreach (var charm in problem.Charms)
            {
                Assert.True(next.CharmPositions[charm.InstanceId] == first.CharmPositions[charm.InstanceId],
                    $"{round + 1}번째 다시 풀기에서 {charm.Definition.Id} 가 옮겨졌다");
            }
        }
    }
}
