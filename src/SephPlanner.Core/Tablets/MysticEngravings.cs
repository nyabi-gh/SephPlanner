using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    public sealed class MysticRule
    {
        public int FirstThreshold { get; set; }
        public int FirstCount { get; set; }
        public int SecondThreshold { get; set; }
        public int SecondCount { get; set; }
        public string Query { get; set; } = "";
        public string ConditionQuery { get; set; } = "";
    }

    /// <summary>서버가 공개한 신비 좌표와 콤보 수로 고정 각인을 복원한다. 최종 레벨을 역산하지 않는다.</summary>
    public static class MysticEngravings
    {
        public static List<FixedEffectCell> Resolve(
            MysticRule rule, int comboCount, IReadOnlyList<GridPos> positions, GridSpec grid)
        {
            var cells = new Dictionary<GridPos, FixedEffectCell>();
            if (comboCount >= rule.FirstThreshold) Add(0, rule.FirstCount);
            if (comboCount >= rule.SecondThreshold) Add(rule.FirstCount, rule.SecondCount);
            return new List<FixedEffectCell>(cells.Values);

            void Add(int start, int count)
            {
                for (var i = start; i < start + count; i++)
                {
                    // 좌표 동기화가 아직 끝나지 않았으면 추측한 칸에 효과를 심지 않는다.
                    if (i < 0 || i >= positions.Count) continue;
                    var origin = positions[i];
                    if (TabletQuery.Parse(rule.ConditionQuery, grid, origin, 0).Count != 0)
                        throw new InvalidOperationException("조건이 있는 신비 각인은 아직 복원할 수 없습니다.");

                    foreach (var effect in TabletQuery.Parse(rule.Query, grid, origin, 0))
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
        }
    }
}
