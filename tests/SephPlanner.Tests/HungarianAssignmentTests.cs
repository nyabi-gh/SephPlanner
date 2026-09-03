using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 배정기가 정말 최적해를 내는지.
///
/// <b>왜 따로 세우는가.</b> 이 프로젝트에서 "최적"이라고 말할 수 있는 자리는 여기 하나다 -
/// 석판이 정해지면 아티팩트 배치는 배정 문제라 정확히 풀린다는 것이 <see cref="PlacementSolver"/>
/// 의 근거이고, 화면에 뜨는 모든 점수와 추천이 그 위에 얹혀 있다. 그런데 배정이 미묘하게
/// 어긋나면 <b>아무 테스트도 실패하지 않은 채</b> 추천만 조용히 나빠진다. 솔버를 통한 간접
/// 확인으로는 잡히지 않는 종류의 회귀다.
///
/// 그래서 무차별 대입으로 구한 최적해와 직접 견준다. 비용에 <b>음수와 소수</b>를 섞는 것은
/// 실제로 그런 값이 들어오기 때문이다 - 솔버는 점수를 뒤집어 넣으므로 비용이 대부분 음수다.
/// </summary>
public class HungarianAssignmentTests
{
    [Fact]
    public void TheAssignmentMatchesBruteForceOnRandomMatrices()
    {
        var random = new Random(20260903);

        for (var trial = 0; trial < 2000; trial++)
        {
            var columns = random.Next(1, 7);
            var rows = random.Next(1, columns + 1);
            var cost = new double[rows, columns];
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                    cost[row, column] = random.Next(-50, 51) + (random.Next(0, 2) == 0 ? 0.0 : 0.5);
            }

            var assignment = HungarianAssignment.Solve(cost, out var total);

            Assert.Equal(rows, assignment.Length);

            // 행은 모두 배정돼야 하고 열은 겹치면 안 된다. 그리고 돌려준 합이 실제 배정의 합이어야 한다.
            var taken = new HashSet<int>();
            var recomputed = 0.0;
            for (var row = 0; row < rows; row++)
            {
                Assert.InRange(assignment[row], 0, columns - 1);
                Assert.True(taken.Add(assignment[row]), $"열 {assignment[row]} 이 두 번 배정됐다.");
                recomputed += cost[row, assignment[row]];
            }

            Assert.Equal(recomputed, total, 9);
            Assert.Equal(BestPossible(cost, rows, columns), total, 9);
        }
    }

    /// <summary>한 칸짜리부터 정사각까지, 손으로 답을 아는 모양들.</summary>
    [Fact]
    public void SmallShapesAreSolvedExactly()
    {
        Assert.Equal(-3, Total(new[,] { { -3.0 } }));

        // 정사각. 대각선(1+1)보다 반대 대각선(0+0)이 싸다.
        Assert.Equal(0, Total(new[,] { { 1.0, 0.0 }, { 0.0, 1.0 } }));

        // 행보다 열이 많다. 남는 열은 버리고 싼 둘을 고른다.
        Assert.Equal(-9, Total(new[,] { { -4.0, -1.0, 0.0 }, { -1.0, -5.0, 0.0 } }));

        // 값이 전부 같으면 어느 배정이든 합이 같아야 한다.
        Assert.Equal(6, Total(new[,] { { 2.0, 2.0, 2.0 }, { 2.0, 2.0, 2.0 }, { 2.0, 2.0, 2.0 } }));
    }

    /// <summary>행이 열보다 많으면 배정 자체가 성립하지 않는다. 조용히 잘라내면 안 된다.</summary>
    [Fact]
    public void MoreRowsThanColumnsIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => HungarianAssignment.Solve(new[,] { { 1.0 }, { 2.0 } }, out _));
    }

    private static double Total(double[,] cost)
    {
        HungarianAssignment.Solve(cost, out var total);
        return total;
    }

    private static double BestPossible(double[,] cost, int rows, int columns) =>
        Search(cost, rows, columns, 0, new bool[columns]);

    private static double Search(double[,] cost, int rows, int columns, int row, bool[] taken)
    {
        if (row == rows) return 0;

        var best = double.MaxValue;
        for (var column = 0; column < columns; column++)
        {
            if (taken[column]) continue;

            taken[column] = true;
            var rest = Search(cost, rows, columns, row + 1, taken);
            taken[column] = false;

            if (cost[row, column] + rest < best) best = cost[row, column] + rest;
        }
        return best;
    }
}
