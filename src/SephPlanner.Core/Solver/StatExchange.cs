using System;
using System.Collections.Generic;
using System.Linq;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 능력치를 "아티팩트 레벨 몇 개어치인가"로 옮기는 환산율.
    ///
    /// 아티팩트도 콤보도 같은 <c>StatusDatabase</c> 체계를 쓴다는 점이 다리다. 능력치별로
    /// "레벨 하나가 이 능력치를 얼마 올려 주는가"를 구해 두면, 능력치로 표현된 것은 무엇이든
    /// 레벨 단위로 옮길 수 있다. 콤보 가중치(<see cref="ComboWorthMeasure"/>)와 아티팩트
    /// 가치(<see cref="CharmStatWorth"/>)가 같은 표를 쓰므로 두 값이 같은 자로 잰 값이 된다.
    /// </summary>
    public sealed class StatExchange
    {
        /// <summary>능력치별 레벨 하나당 증가분. 아무 아티팩트도 주지 않는 능력치는 없다.</summary>
        public Dictionary<string, double> PerLevel { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>그 환산율을 뒷받침한 아티팩트 수. 1이면 그 아티팩트 자신뿐이라 환산이 동어반복이다.</summary>
        public Dictionary<string, int> Samples { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// 환산율을 믿을 만하다고 볼 최소 표본 수. 표본이 이보다 적으면 환산은 되지만 그 값은
        /// 사실상 "평균쯤 되겠거니"라는 뜻이므로, 결과에 그렇게 표시한다.
        /// </summary>
        public const int ReliableSamples = 3;

        public bool IsReliable(string statusId) =>
            Samples.TryGetValue(statusId, out var count) && count >= ReliableSamples;

        /// <summary>레벨 단위로 옮긴 값. 환산율이 없는 능력치는 <c>false</c>.</summary>
        public bool TryConvert(string statusId, double value, out double levels)
        {
            levels = 0;
            if (!PerLevel.TryGetValue(statusId, out var perLevel) || perLevel <= 0) return false;

            levels = value / perLevel;
            return true;
        }

        /// <summary>
        /// 아티팩트들의 레벨별 능력치 표에서 환산율을 뽑는다. 같은 능력치를 주는 아티팩트가
        /// 여럿이라 중앙값을 쓴다 - 평균은 유별나게 센 아티팩트 하나에 끌려간다.
        /// </summary>
        public static StatExchange From(IEnumerable<CharmStatTable> tables)
        {
            var samples = new Dictionary<string, List<double>>(StringComparer.Ordinal);

            foreach (var table in tables)
            {
                // 레벨 0 은 그 아티팩트의 기본값이다. 한 걸음의 크기를 재는 것이므로 증가분만 본다.
                var steps = new List<double>();
                for (var level = 1; level < table.ValuesByLevel.Count; level++)
                {
                    var step = table.ValuesByLevel[level] - table.ValuesByLevel[level - 1];
                    if (step > 0) steps.Add(step);
                }
                if (steps.Count == 0) continue;

                if (!samples.TryGetValue(table.StatusId, out var list))
                    samples[table.StatusId] = list = new List<double>();
                list.Add(Median(steps));
            }

            var exchange = new StatExchange();
            foreach (var pair in samples)
            {
                exchange.PerLevel[pair.Key] = Median(pair.Value);
                exchange.Samples[pair.Key] = pair.Value.Count;
            }
            return exchange;
        }

        public static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;

            var sorted = values.OrderBy(value => value).ToList();
            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 1
                ? sorted[middle]
                : (sorted[middle - 1] + sorted[middle]) / 2;
        }
    }
}
