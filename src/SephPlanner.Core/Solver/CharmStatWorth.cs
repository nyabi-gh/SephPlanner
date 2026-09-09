using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>한 아티팩트가 레벨마다 실제로 주는 값어치. 레벨 단위다.</summary>
    public sealed class CharmWorthTable
    {
        public int EntityId { get; set; }

        /// <summary>색인이 곧 레벨이다. 게임의 <c>LevelToIdx</c>와 같게 상한에서 자른다.</summary>
        public List<double> ByLevel { get; set; } = new List<double>();
        public List<double> BenefitByLevel { get; set; } = new List<double>();
        public List<double> PenaltyByLevel { get; set; } = new List<double>();

        /// <summary>
        /// 환산에 쓴 능력치 중 표본이 넉넉했던 것의 비율(0~1). 낮으면 그 아티팩트만 그 능력치를
        /// 주어서 "레벨 하나 = 이 아티팩트의 한 걸음"이라는 동어반복에 가깝다는 뜻이다.
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>환산율이 없어 값에 반영하지 못한 능력치.</summary>
        public List<string> Unconverted { get; set; } = new List<string>();
    }

    public sealed class CharmWorthReport
    {
        public StatExchange Exchange { get; set; } = new StatExchange();
        public Dictionary<int, CharmWorthTable> ByEntity { get; set; } = new Dictionary<int, CharmWorthTable>();
    }

    /// <summary>
    /// 아티팩트가 레벨마다 주는 능력치를 레벨 단위 값어치로 옮긴다.
    ///
    /// 점수 모델은 아티팩트를 "켜져 있으면 1점, 레벨 하나에 1점"으로 센다. 그 모델은 모든
    /// 아티팩트가 레벨당 같은 값을 준다고 가정하는데, 실제로 재 보면 그렇지 않다 - 레벨 하나가
    /// 중앙값의 다섯 배인 것도 있고, 레벨을 올릴수록 나빠지는 것도 있다. 여기서 나온 표가
    /// 그 가정을 대신한다.
    ///
    /// <see cref="ComboWorthMeasure"/>와 같은 <see cref="StatExchange"/>를 쓰므로 결과가
    /// <see cref="Worth.ComboThreshold"/>·<see cref="Worth.DamageBonus"/>와 같은 자로 잰 값이다.
    /// </summary>
    public static class CharmStatWorth
    {
        /// <summary>
        /// 잰 값을 아티팩트 정의에 실어 둔다. 이후 솔버는 표를 그대로 읽기만 한다.
        /// </summary>
        public static CharmWorthReport Apply(
            IReadOnlyCollection<CharmDefinition> charms, StatMeasurement measurement)
        {
            var maxLevels = new Dictionary<int, int>();
            foreach (var charm in charms) maxLevels[charm.EntityId] = charm.MaxLevel;

            var report = Run(measurement, entityId => maxLevels.TryGetValue(entityId, out var max) ? max : -1);

            foreach (var charm in charms)
            {
                charm.StatEffects.Clear();
                foreach (var table in measurement.CharmStats)
                {
                    if (table.EntityId != charm.EntityId) continue;
                    charm.StatEffects.Add(new CharmStatEffect
                    {
                        StatusId = table.StatusId,
                        AmountByLevel = new List<int>(table.ValuesByLevel),
                        WorthPerUnit = report.Exchange.TryConvert(table.StatusId, 1, out var perUnit) ? perUnit : (double?)null,
                        Samples = report.Exchange.Samples.TryGetValue(table.StatusId, out var samples) ? samples : 0,
                    });
                }
                foreach (var bonus in charm.ContextStats)
                    bonus.WorthPerUnit = report.Exchange.TryConvert(bonus.StatusId, 1, out var unit) ? unit : (double?)null;
                charm.StatWorthByLevel.Clear();
                charm.StatBenefitByLevel.Clear();
                charm.StatPenaltyByLevel.Clear();
                charm.StatWorthConfidence = 0;
                charm.StatWorthCoverageKnown = true;
                charm.StatWorthUnconverted.Clear();
                if (!report.ByEntity.TryGetValue(charm.EntityId, out var worth)) continue;

                charm.StatWorthByLevel = worth.ByLevel;
                charm.StatBenefitByLevel = worth.BenefitByLevel;
                charm.StatPenaltyByLevel = worth.PenaltyByLevel;
                charm.StatWorthConfidence = worth.Confidence;
                charm.StatWorthUnconverted.AddRange(worth.Unconverted);
            }
            return report;
        }

        /// <summary>
        /// <paramref name="maxLevelOf"/>가 알려진 아티팩트는 게임의 레벨 상한에서 표를 자른다.
        /// </summary>
        public static CharmWorthReport Run(StatMeasurement measurement, Func<int, int>? maxLevelOf = null)
        {
            var exchange = StatExchange.From(measurement.CharmStats);
            var report = new CharmWorthReport { Exchange = exchange };

            var byEntity = new Dictionary<int, List<CharmStatTable>>();
            foreach (var table in measurement.CharmStats)
            {
                if (!byEntity.TryGetValue(table.EntityId, out var list))
                    byEntity[table.EntityId] = list = new List<CharmStatTable>();
                list.Add(table);
            }

            foreach (var pair in byEntity)
            {
                var tables = pair.Value;
                var span = 0;
                foreach (var table in tables) span = Math.Max(span, table.ValuesByLevel.Count);

                // 상한을 모르는 아티팩트(음수)는 표 전체를 쓴다. 상한이 0 이면 레벨을 올려도
                // 값이 바뀌지 않으므로 한 칸만 남는다.
                var cap = maxLevelOf?.Invoke(pair.Key) ?? -1;
                var top = cap >= 0 ? Math.Min(cap, span - 1) : span - 1;

                var worth = new CharmWorthTable { EntityId = pair.Key };
                double reliable = 0, total = 0;

                for (var level = 0; level <= top; level++)
                {
                    double sum = 0, benefit = 0, penalty = 0;
                    foreach (var table in tables)
                    {
                        if (table.ValuesByLevel.Count == 0) continue;

                        var value = table.ValuesByLevel[Math.Min(level, table.ValuesByLevel.Count - 1)];
                        if (!exchange.TryConvert(table.StatusId, value, out var levels))
                        {
                            if (value != 0 && !worth.Unconverted.Contains(table.StatusId))
                                worth.Unconverted.Add(table.StatusId);
                            continue;
                        }

                        sum += levels;
                        benefit += Math.Max(0, levels);
                        penalty += Math.Min(0, levels);

                        // 값어치가 어디서 왔는지를 크기로 잰다. 부호는 상관없다 - 깎는 능력치도
                        // 그 환산율을 믿을 수 있어야 깎는 만큼을 믿을 수 있다.
                        total += Math.Abs(levels);
                        if (exchange.IsReliable(table.StatusId)) reliable += Math.Abs(levels);
                    }
                    worth.ByLevel.Add(sum);
                    worth.BenefitByLevel.Add(benefit);
                    worth.PenaltyByLevel.Add(penalty);
                }

                worth.Confidence = total > 0 ? reliable / total : 0;
                report.ByEntity[pair.Key] = worth;
            }
            return report;
        }
    }
}
