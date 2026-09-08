using System;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 행 개수 이하의 열을 가진 비용 행렬에 대한 최적 배정. 헝가리안 알고리즘의 전위 기반 구현이다.
    ///
    /// 석판 배치가 정해지면 칸마다 레벨이 확정되므로, 어떤 아티팩트를 어느 칸에 둘지는
    /// 조합 탐색이 아니라 배정 문제가 된다. 그래서 이 부분만은 최적해를 항상 얻을 수 있다.
    /// </summary>
    public static class HungarianAssignment
    {
        /// <summary>정수 우선 비용을 먼저 최소화하고, 동률에서만 원래 비용을 비교한다.</summary>
        public static int[] SolvePrioritized(double[,] cost, int[,] priority)
        {
            var rows = cost.GetLength(0);
            var columns = cost.GetLength(1);
            if (priority.GetLength(0) != rows || priority.GetLength(1) != columns)
                throw new ArgumentException("우선 비용 행렬의 크기가 다릅니다.", nameof(priority));
            var scale = 0.0;
            foreach (var value in cost) scale = Math.Max(scale, Math.Abs(value));
            var combined = new double[rows, columns];
            // 배정 전체의 원래 비용 차이를 1 미만으로 제한해 정수 우선순위를 뒤집지 못하게 한다.
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                    combined[row, column] = priority[row, column] +
                        (scale == 0 ? 0 : cost[row, column] / scale / (2.0 * rows + 1));
            return Solve(combined, out _);
        }

        private const double Infinity = double.MaxValue / 4;

        /// <summary>
        /// <paramref name="cost"/>는 [행, 열] 비용이며 행 수가 열 수보다 많으면 안 된다.
        /// 반환값은 행마다 배정된 열 번호, 배정이 없으면 -1.
        /// </summary>
        public static int[] Solve(double[,] cost, out double totalCost)
        {
            var rows = cost.GetLength(0);
            var columns = cost.GetLength(1);
            if (rows > columns) throw new ArgumentException("행 수가 열 수보다 많습니다.", nameof(cost));

            var rowPotential = new double[rows + 1];
            var columnPotential = new double[columns + 1];
            var columnMatch = new int[columns + 1];
            var path = new int[columns + 1];

            for (var row = 1; row <= rows; row++)
            {
                columnMatch[0] = row;
                var current = 0;
                var minimum = new double[columns + 1];
                var used = new bool[columns + 1];
                for (var i = 0; i <= columns; i++) minimum[i] = Infinity;

                do
                {
                    used[current] = true;
                    var matchedRow = columnMatch[current];
                    var delta = Infinity;
                    var next = 0;

                    for (var column = 1; column <= columns; column++)
                    {
                        if (used[column]) continue;

                        var reduced = cost[matchedRow - 1, column - 1]
                                      - rowPotential[matchedRow] - columnPotential[column];
                        if (reduced < minimum[column])
                        {
                            minimum[column] = reduced;
                            path[column] = current;
                        }
                        if (minimum[column] < delta)
                        {
                            delta = minimum[column];
                            next = column;
                        }
                    }

                    for (var column = 0; column <= columns; column++)
                    {
                        if (used[column])
                        {
                            rowPotential[columnMatch[column]] += delta;
                            columnPotential[column] -= delta;
                        }
                        else
                        {
                            minimum[column] -= delta;
                        }
                    }
                    current = next;
                }
                while (columnMatch[current] != 0);

                do
                {
                    var previous = path[current];
                    columnMatch[current] = columnMatch[previous];
                    current = previous;
                }
                while (current != 0);
            }

            var assignment = new int[rows];
            for (var i = 0; i < rows; i++) assignment[i] = -1;

            totalCost = 0;
            for (var column = 1; column <= columns; column++)
            {
                var row = columnMatch[column];
                if (row == 0) continue;
                assignment[row - 1] = column - 1;
                totalCost += cost[row - 1, column - 1];
            }
            return assignment;
        }
    }
}
