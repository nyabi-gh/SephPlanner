using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 콤보 수에서 살아 있는 콤보 각인의 칸 효과를 푼다. 게임의 <c>ComboEffect_Mystic.OnEnableEffect</c>
    /// 와 같은 순서다 - 문턱을 넘을 때마다 좌표 목록의 다음 칸들에 각인을 하나씩 심는다.
    /// </summary>
    public static class ComboEngravings
    {
        /// <summary>이 수에서 넘은 문턱의 수. 같은 단계면 칸도 같으므로 풀이는 단계로 묶어 둔다.</summary>
        public static int StageAt(ComboEngravingRule rule, int count)
        {
            var stage = 0;
            foreach (var tier in rule.Tiers)
            {
                if (count < tier.Threshold) break;
                stage++;
            }
            return stage;
        }

        public static List<FixedEffectCell> Cells(ComboEngravingRule rule, int stage, GridSpec grid)
        {
            var cells = new Dictionary<GridPos, FixedEffectCell>();
            var next = 0;
            for (var index = 0; index < stage && index < rule.Tiers.Count; index++)
            {
                for (var i = 0; i < rule.Tiers[index].Count; i++, next++)
                {
                    // 게임은 좌표가 모자라면 (0,0) 에 심고 오류만 남긴다. 참가자는 좌표 동기화가
                    // 끝나기 전에도 여기 오므로, 모르는 칸에 효과를 지어내지 않고 건너뛴다.
                    if (next >= rule.Positions.Count) continue;

                    foreach (var effect in TabletQuery.Parse(rule.Query, grid, rule.Positions[next], 0))
                    {
                        if (!grid.Contains(effect.Position)) continue;
                        if (!cells.TryGetValue(effect.Position, out var cell))
                            cells[effect.Position] = cell = new FixedEffectCell { Position = effect.Position };
                        var (kind, amount) = QueryValue.ReadEffect(effect.Value);
                        switch (kind)
                        {
                            case TabletEffectKind.IncreaseConstLevel: cell.Level += amount; break;
                            case TabletEffectKind.Disable: cell.Disable++; break;
                            case TabletEffectKind.IgnoreCriteria: cell.IgnoreCriteria++; break;
                            case TabletEffectKind.MultiplyConstLevel: cell.Multiply += amount; break;
                        }
                    }
                }
            }
            return new List<FixedEffectCell>(cells.Values);
        }

        /// <summary>두 층을 칸별로 더한다. 각인끼리는 덧셈으로만 쌓인다(<c>ReleasePermission</c>).</summary>
        public static List<FixedEffectCell> Combine(
            IReadOnlyList<FixedEffectCell> first, IReadOnlyList<FixedEffectCell> second)
        {
            var cells = new Dictionary<GridPos, FixedEffectCell>();
            var order = new List<GridPos>();
            foreach (var layer in new[] { first, second })
            {
                foreach (var source in layer)
                {
                    if (!cells.TryGetValue(source.Position, out var cell))
                    {
                        cells[source.Position] = cell = new FixedEffectCell { Position = source.Position };
                        order.Add(source.Position);
                    }
                    cell.Level += source.Level;
                    cell.Multiply += source.Multiply;
                    cell.Disable += source.Disable;
                    cell.IgnoreCriteria += source.IgnoreCriteria;
                }
            }
            var result = new List<FixedEffectCell>(order.Count);
            foreach (var position in order) result.Add(cells[position]);
            return result;
        }

        /// <summary>
        /// 되뺀 층에서 콤보 각인 몫을 뺀다. 음수가 남으면 게임이 아직 그 각인을 심지 않은 순간이라
        /// 판단을 미룬다 - 콤보 수와 행렬은 따로 동기화되어 한 폴링 안에서 어긋날 수 있다.
        /// </summary>
        public static FixedEffectResidualResult Without(
            FixedEffectResidualResult residual, IReadOnlyList<FixedEffectCell> engraved)
        {
            if (residual.Status != FixedEffectResidualStatus.Extracted || engraved.Count == 0) return residual;

            var cells = new Dictionary<GridPos, FixedEffectCell>();
            var order = new List<GridPos>();
            foreach (var source in residual.Cells)
            {
                cells[source.Position] = new FixedEffectCell
                {
                    Position = source.Position,
                    Level = source.Level,
                    Multiply = source.Multiply,
                    Disable = source.Disable,
                    IgnoreCriteria = source.IgnoreCriteria,
                };
                order.Add(source.Position);
            }

            foreach (var source in engraved)
            {
                if (!cells.TryGetValue(source.Position, out var cell))
                {
                    cells[source.Position] = cell = new FixedEffectCell { Position = source.Position };
                    order.Add(source.Position);
                }
                cell.Level -= source.Level;
                cell.Multiply -= source.Multiply;
                cell.Disable -= source.Disable;
                cell.IgnoreCriteria -= source.IgnoreCriteria;
                if (cell.Multiply < 0 || cell.Disable < 0 || cell.IgnoreCriteria < 0)
                {
                    return new FixedEffectResidualResult
                    {
                        Status = FixedEffectResidualStatus.Unsettled,
                        Reason = $"콤보 각인 칸 {source.Position} 의 효과가 게임 행렬에 아직 없습니다",
                    };
                }
            }

            var result = new List<FixedEffectCell>();
            foreach (var position in order)
            {
                var cell = cells[position];
                if (cell.Level != 0 || cell.Multiply != 0 || cell.Disable != 0 || cell.IgnoreCriteria != 0)
                    result.Add(cell);
            }
            return new FixedEffectResidualResult { Status = FixedEffectResidualStatus.Extracted, Cells = result };
        }
    }
}
