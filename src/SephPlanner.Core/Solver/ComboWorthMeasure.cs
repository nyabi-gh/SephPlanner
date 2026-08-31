using System.Collections.Generic;
using System.Linq;

namespace SephPlanner.Core.Solver
{
    /// <summary>아티팩트 하나가 레벨에 따라 올려 주는 능력치 표. 게임 <c>Charm_StatusInstance.stats</c>.</summary>
    public sealed class CharmStatTable
    {
        public int EntityId { get; set; }
        public string StatusId { get; set; } = "";
        public List<int> ValuesByLevel { get; set; } = new List<int>();
    }

    /// <summary>콤보가 임계값에서 주는 능력치 하나. 게임 <c>ComboEffectBase.addStatByCombo</c>.</summary>
    public sealed class ComboStatGrant
    {
        public string CategoryId { get; set; } = "";
        public int Threshold { get; set; }
        public string StatusId { get; set; } = "";
        public int Value { get; set; }
    }

    /// <summary>콤보 가중치를 재기 위해 게임에서 그대로 떠 온 숫자들.</summary>
    public sealed class StatMeasurement
    {
        public List<CharmStatTable> CharmStats { get; set; } = new List<CharmStatTable>();
        public List<ComboStatGrant> ComboStats { get; set; } = new List<ComboStatGrant>();
    }

    public sealed class ThresholdWorth
    {
        public string CategoryId { get; set; } = "";
        public int Threshold { get; set; }

        /// <summary>레벨 몇 개 값어치인지. 환산하지 못한 능력치는 빠져 있다.</summary>
        public double Levels { get; set; }

        /// <summary>아티팩트 쪽에 같은 능력치가 없어 환산하지 못한 것들.</summary>
        public List<string> Unconverted { get; set; } = new List<string>();
    }

    public sealed class MeasurementReport
    {
        public Dictionary<string, double> PerLevel { get; set; } = new Dictionary<string, double>();
        public List<ThresholdWorth> Thresholds { get; set; } = new List<ThresholdWorth>();

        /// <summary>환산된 임계값들의 중앙값. <see cref="Worth.ComboThreshold"/>가 되어야 할 값.</summary>
        public double MedianLevels { get; set; }

        public int ConvertedCount { get; set; }
        public int UnconvertedCount { get; set; }
    }

    /// <summary>
    /// 콤보 한 단계가 아티팩트 레벨 몇 개 값어치인지 잰다.
    ///
    /// 두 쪽이 같은 능력치 체계(<c>StatusDatabase</c>)를 쓴다는 점이 다리가 된다. 아티팩트는
    /// 레벨마다 능력치를 얼마씩 올려 주고, 콤보는 임계값마다 능력치를 얼마 준다. 그러니 능력치별로
    /// "레벨 하나당 얼마"를 먼저 구하면 콤보가 주는 값을 레벨 단위로 옮길 수 있다.
    ///
    /// 이렇게까지 하는 이유는 <see cref="Worth.ComboThreshold"/>가 근거 없는 설계값이었기
    /// 때문이다. 추천이 콤보 쪽으로 얼마나 기울지를 정하는 값이라 짐작으로 두면 안 된다.
    /// </summary>
    public static class ComboWorthMeasure
    {
        public static MeasurementReport Run(StatMeasurement measurement)
        {
            var exchange = StatExchange.From(measurement.CharmStats);
            var report = new MeasurementReport { PerLevel = exchange.PerLevel };

            var grouped = measurement.ComboStats
                .GroupBy(grant => (grant.CategoryId, grant.Threshold))
                .OrderBy(group => group.Key.CategoryId)
                .ThenBy(group => group.Key.Threshold);

            foreach (var group in grouped)
            {
                var worth = new ThresholdWorth
                {
                    CategoryId = group.Key.CategoryId,
                    Threshold = group.Key.Threshold,
                };

                foreach (var grant in group)
                {
                    if (exchange.TryConvert(grant.StatusId, grant.Value, out var levels))
                        worth.Levels += levels;
                    else
                        worth.Unconverted.Add($"{grant.StatusId}/{grant.Value}");
                }

                if (worth.Levels > 0) report.ConvertedCount++;
                else report.UnconvertedCount++;

                report.Thresholds.Add(worth);
            }

            report.MedianLevels = StatExchange.Median(report.Thresholds
                .Where(entry => entry.Levels > 0)
                .Select(entry => entry.Levels)
                .ToList());

            return report;
        }
    }
}
