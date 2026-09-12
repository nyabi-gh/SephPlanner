using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 다듬기는 교환 하나가 실제로 바꾸는 것만 다시 본다 - 점유는 옮긴 두 칸만 고치고, 석판 효과
/// 행렬은 그 두 칸을 조건으로 읽는 석판이 있을 때만 다시 만든다.
///
/// <b>이 최적화의 위험은 느려지는 것이 아니라 조용히 다른 답을 내는 것이다.</b> "무엇이 안
/// 바뀌는가"를 한 자리만 잘못 짚어도 점수가 달라지고, 그러면 추천이 통째로 흔들린다. 그래서
/// 무작위 판으로 증분 경로와 전체 재계산 경로를 대조한다.
/// </summary>
public class PolishIncrementTests
{
    /// <summary>
    /// 판정이 <b>점유를 읽는 칸</b> 밖에서는 무엇이 달라져도 판정이 안 바뀐다. 증분 경로가
    /// 시뮬레이션을 건너뛰는 근거가 이것뿐이라, 여기가 깨지면 나머지도 같이 깨진다.
    /// </summary>
    [Theory]
    [InlineData("RIGHT CHARM")]
    [InlineData("LEFT ITEM\nRIGHT CHARM")]
    [InlineData("O ITEM")]
    [InlineData("IDX 3 PLACED\nUP CHARM")]
    [InlineData("")]
    public void CriteriaOnlyReadTheCellsItReports(string condition)
    {
        var grid = new GridSpec(4, 3, 12);
        var placement = new TabletSlot
        {
            InstanceId = 1,
            Definition = new TabletDefinition { Id = "t", Query = "RIGHT 1", ConditionQuery = condition },
        }.At(new GridPos(1, 1), 0);

        var read = new List<GridPos>();
        TabletSimulator.CriteriaCells(placement, grid, read);

        // 보고한 칸들만 정해 두고, 나머지 칸을 하나씩 채웠다 비웠다 해도 판정이 그대로여야 한다.
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var cell = new GridPos(x, y);
                if (read.Contains(cell)) continue;

                var empty = new GridOccupancy();
                var filled = new GridOccupancy();
                filled.AddItem(cell, isCharm: true, isMagicCharm: true);

                Assert.Equal(
                    TabletSimulator.MeetsCriteria(placement, empty, grid),
                    TabletSimulator.MeetsCriteria(placement, filled, grid));
            }
    }

    /// <summary>
    /// 무작위 판을 풀되 교환마다 통째로 다시 만든 점유·행렬과 대조한다. 어긋나면 그 자리에서
    /// 던지므로, 이 테스트가 통과한다는 것은 증분 경로가 전체 재계산과 한 칸도 다르지 않았다는
    /// 뜻이다. 답 자체도 대조 없이 푼 것과 견준다.
    /// </summary>
    [Fact]
    public void TheIncrementalPathMatchesAFullRebuildOnRandomBoards()
    {
        var checkedBoards = 0;
        for (var seed = 0; seed < 40; seed++)
        {
            var problem = RandomBoard(seed);
            var verified = PlacementSolver.Solve(problem, new SolverOptions { VerifyIncrementalPolish = true });
            var plain = PlacementSolver.Solve(RandomBoard(seed), new SolverOptions());

            Assert.Equal(plain.Score, verified.Score, 12);
            Assert.Equal(
                plain.CharmPositions.OrderBy(pair => pair.Key).ToList(),
                verified.CharmPositions.OrderBy(pair => pair.Key).ToList());
            Assert.Equal(
                plain.TabletPositions.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value.Position, pair.Value.Rotation)).ToList(),
                verified.TabletPositions.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value.Position, pair.Value.Rotation)).ToList());
            checkedBoards++;
        }
        Assert.Equal(40, checkedBoards);
    }

    /// <summary>
    /// 조건이 걸린 석판과 필러·마법 아티팩트를 섞는다. 셋이 섞여야 교환이 점유의 세 갈래
    /// (아이템·아티팩트·마법 아티팩트)를 모두 흔들고, 그래야 판정이 실제로 뒤집힌다.
    /// </summary>
    private static PlacementProblem RandomBoard(int seed)
    {
        var random = new Random(seed * 977 + 13);
        var width = 4 + random.Next(2);
        var height = 3 + random.Next(2);
        var storage = width * height - random.Next(3);
        var problem = new PlacementProblem { Grid = new GridSpec(width, height, storage) };

        var conditions = new[] { "", "RIGHT CHARM", "LEFT ITEM", "O CHARM", "UP ITEM\nDOWN CHARM" };
        var effects = new[] { "RIGHT 1", "O 1", "LEFT 2\nRIGHT 1", "UP -1", "O DISABLE" };
        var tablets = 1 + random.Next(3);
        for (var index = 0; index < tablets; index++)
            problem.Tablets.Add(new TabletSlot
            {
                InstanceId = 900 + index,
                Definition = new TabletDefinition
                {
                    Id = "t" + index,
                    EntityId = 700 + index,
                    Query = effects[random.Next(effects.Length)],
                    ConditionQuery = conditions[random.Next(conditions.Length)],
                },
            });

        var charms = Math.Max(1, storage - tablets - random.Next(3));
        for (var index = 0; index < charms; index++)
        {
            var filler = random.Next(5) == 0;
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = index,
                IsFiller = filler,
                Enchant = random.Next(-1, 2),
                Definition = new CharmDefinition
                {
                    Id = "c" + index,
                    EntityId = 200 + index,
                    MaxLevel = 3,
                    IsMagic = random.Next(3) == 0,
                },
                Worth = new CharmWorth { ByLevel = Enumerable.Range(0, 4).Select(_ => (double)random.Next(-2, 9)).ToArray() },
            });
        }

        // 시작 자리를 흩어 놓는다. 앵커가 있어야 다듬기가 실제로 교환을 시도한다.
        var cells = Enumerable.Range(0, storage).Select(problem.Grid.ToPosition).OrderBy(_ => random.Next()).ToList();
        var at = 0;
        foreach (var tablet in problem.Tablets)
            problem.CurrentTablets[tablet.InstanceId] = new TabletSpot(cells[at++], 0);
        foreach (var charm in problem.Charms)
            if (at < cells.Count) problem.CurrentCharms[charm.InstanceId] = cells[at++];
        return problem;
    }
}
