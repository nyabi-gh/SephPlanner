using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 점수의 눈금. 넷 다 게임 자료에서 잰 값이라 게임이 균형을 고치면 함께 움직인다.
    ///
    /// 예전에는 넷이 상수로 박혀 있어서 패치가 올 때마다 사람이 <c>--measure</c> 를 돌려 숫자를
    /// 옮겨 적어야 했다. 안 옮기면 아무 소리 없이 틀린 값으로 점수를 매긴다. 그래서 카탈로그를
    /// 지을 때 재어 실어 두고, 재현 자료도 그때의 눈금을 그대로 들고 다닌다.
    /// </summary>
    public sealed class WorthScale
    {
        /// <summary>
        /// 같은 카테고리를 하나 더 모아 콤보가 발동할 때의 가치. 콤보가 임계값에서 주는 능력치를
        /// <see cref="StatExchange"/>로 레벨로 옮긴 뒤 임계값들의 중앙값을 쓴다.
        /// </summary>
        public double ComboThreshold { get; set; } = 2.59;

        /// <summary>
        /// 아직 임계값에 못 미치지만 한 걸음 다가가는 가치. 이쪽은 잰 값이 아니다 - 못 채운
        /// 콤보의 값어치는 그 판에서 결국 채우게 되느냐에 달려 있어 정적 자료로는 답이 없다.
        /// 비율만 예전 그대로 두고 크기는 <see cref="ComboThreshold"/>를 따라간다.
        /// </summary>
        public double ComboProgress { get; set; } = 0.32;

        /// <summary>
        /// 전체 피해 보너스 1점이 레벨 몇 어치인지. 게임의 <c>StatusInstance_FinalDamage</c>가
        /// 조화의 수정과 똑같은 커스텀 능력치를 더하므로 <c>FINAL_DAMAGE</c>가 그대로 다리다.
        /// </summary>
        public double DamageBonus { get; set; } = 0.26;

        /// <summary>
        /// 점수를 견줄 때 쓰는 눈금. 이보다 작은 차이는 배치의 우열로 보지 않는다.
        ///
        /// 카탈로그 전체에서 이웃 레벨 사이의 값어치 차이 중 가장 작은 것의 절반이다. 눈금이 그
        /// 차이보다 작은 동안은 실제 레벨 한 칸의 우열을 하나도 삼키지 않으며, 절반으로 두는 것은
        /// 환산의 잡음을 최대한 먹으라는 뜻이다.
        /// </summary>
        public double ScoreStep { get; set; } = 0.05;

        /// <summary>못 채운 콤보가 채운 콤보에 대해 갖는 비율. 예전부터 쓰던 설계값이다.</summary>
        public const double ProgressRatio = 1.0 / 8.0;

        /// <summary>잰 눈금이 없을 때 쓰는 값. 마지막으로 재어 적어 둔 수다(게임 1.0.31).</summary>
        public static WorthScale Default { get; } = new WorthScale();

        /// <summary>
        /// 카탈로그에서 눈금을 잰다. 잴 수 없는 항목은 <see cref="Default"/>의 값을 그대로 둔다 -
        /// 게임이 그 자료를 안 실어 줬을 뿐인데 0 으로 두면 그 항목이 통째로 사라진다.
        /// </summary>
        public static WorthScale Measure(
            StatMeasurement measurement, IReadOnlyCollection<CharmDefinition> charms)
        {
            var scale = new WorthScale();
            var profiles = new List<CharmStatProfile>();
            foreach (var charm in charms) profiles.Add(CharmStatWorth.Profile(charm));

            var report = ComboWorthMeasure.Run(measurement, profiles);
            if (report.MedianLevels > 0)
            {
                scale.ComboThreshold = Math.Round(report.MedianLevels, 4);
                scale.ComboProgress = Math.Round(report.MedianLevels * ProgressRatio, 4);
            }
            if (report.Exchange.TryConvert("FINAL_DAMAGE", 1, out var damageBonus) && damageBonus > 0)
                scale.DamageBonus = Math.Round(damageBonus, 4);

            var step = SmallestLevelStep(charms);
            if (step > 0) scale.ScoreStep = step / 2;
            return scale;
        }

        /// <summary>
        /// 이웃 레벨 사이 값어치 차이 중 가장 작은 것. 레벨을 올려도 값이 그대로인 자리는 세지
        /// 않는다 - 우열이 없는 자리라 눈금이 삼켜도 잃는 것이 없다.
        /// </summary>
        private static double SmallestLevelStep(IReadOnlyCollection<CharmDefinition> charms)
        {
            var smallest = 0.0;
            foreach (var charm in charms)
            {
                if (charm.StatWorthByLevel.Count < 2) continue;

                var worth = CharmWorth.Resolve(charm);
                for (var level = 0; level + 1 < charm.StatWorthByLevel.Count; level++)
                {
                    var gap = Math.Abs(worth.At(level + 1) - worth.At(level));
                    if (gap <= 1e-9) continue;
                    if (smallest == 0 || gap < smallest) smallest = gap;
                }
            }
            return smallest;
        }

        /// <summary>
        /// 지금 개수에서 카테고리 하나를 더 모으는 것의 가치. <paramref name="goal"/>은 그때
        /// 노리게 되는 임계값이다. 임계값을 이미 다 넘겼으면 0.
        /// </summary>
        public double OfComboStep(ComboDefinition combo, int currentCount, out bool completes, out int goal)
        {
            completes = false;
            goal = 0;

            var reached = currentCount + 1;
            foreach (var threshold in combo.Thresholds)
            {
                if (threshold < reached) continue;
                if (goal == 0 || threshold < goal) goal = threshold;
            }
            if (goal == 0) return 0;

            completes = goal == reached;
            return completes ? ComboThreshold : ComboProgress;
        }
    }
}
