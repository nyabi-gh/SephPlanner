using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 석판 하나가 격자에 실제로 미치는 효과를 세어 둔 것.
    ///
    /// 점수 증가분만으로는 석판을 고를 수 없을 때가 있다. 아티팩트가 적으면 여러 석판이 똑같이
    /// 최대치를 뽑아내 전부 같은 값으로 보이기 때문이다. 예를 들어 아랫줄 여섯 칸을 올리는 석판과
    /// 세 칸만 올리는 석판은 아티팩트가 셋일 때 증가분이 같지만, 앞으로의 값어치는 전혀 다르다.
    /// </summary>
    public sealed class TabletEffectSummary
    {
        /// <summary>레벨을 올려주는 칸 수와 그 합.</summary>
        public int RaisedCells { get; set; }
        public int RaisedTotal { get; set; }

        /// <summary>레벨을 깎는 칸 수와 그 합(양수로 센다).</summary>
        public int LoweredCells { get; set; }
        public int LoweredTotal { get; set; }

        /// <summary>아예 쓸 수 없게 만드는 칸 수.</summary>
        public int DisabledCells { get; set; }

        /// <summary>배수가 걸리는 칸 수.</summary>
        public int MultipliedCells { get; set; }

        /// <summary>배치 조건을 무시하게 해주는 칸 수.</summary>
        public int IgnoreCriteriaCells { get; set; }

        public bool IsEmpty =>
            RaisedCells == 0 && LoweredCells == 0 && DisabledCells == 0 &&
            MultipliedCells == 0 && IgnoreCriteriaCells == 0;

        /// <summary>
        /// 증가분이 같을 때 순위를 가르는 값. 올려주는 총량에서 깎는 총량을 뺀다.
        /// 지금 당장의 점수가 아니라 이 석판이 가진 여력을 나타낸다.
        /// </summary>
        public int Reach => RaisedTotal - LoweredTotal;

        /// <summary>
        /// 질의를 실제로 놓인 자리와 회전으로 풀어 센다. 격자 밖으로 나가는 칸은 효과가 없으므로
        /// 빼고, 열려 있지 않은 칸도 빼야 실제와 맞는다.
        /// </summary>
        public static TabletEffectSummary Of(string query, GridSpec grid, GridPos position, int rotation)
        {
            var summary = new TabletEffectSummary();
            if (string.IsNullOrEmpty(query)) return summary;

            foreach (var cell in TabletQuery.Parse(query, grid, position, rotation))
            {
                if (!IsUsable(cell.Position, grid)) continue;

                var (kind, level) = QueryValue.ReadEffect(cell.Value);
                switch (kind)
                {
                    case TabletEffectKind.IncreaseConstLevel when level > 0:
                        summary.RaisedCells++;
                        summary.RaisedTotal += level;
                        break;

                    case TabletEffectKind.IncreaseConstLevel when level < 0:
                        summary.LoweredCells++;
                        summary.LoweredTotal += -level;
                        break;

                    case TabletEffectKind.Disable:
                        summary.DisabledCells++;
                        break;

                    case TabletEffectKind.MultiplyConstLevel:
                        summary.MultipliedCells++;
                        break;

                    case TabletEffectKind.IgnoreCriteria:
                        summary.IgnoreCriteriaCells++;
                        break;
                }
            }
            return summary;
        }

        private static bool IsUsable(GridPos position, GridSpec grid) =>
            position.X >= 0 && position.X < grid.Width &&
            position.Y >= 0 && position.Y < grid.Height &&
            grid.ToIndex(position.X, position.Y) < grid.Storage;
    }
}
