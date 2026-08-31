using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 아티팩트의 상대 가치. 모든 아티팩트의 활성 1점을 같게 치면 레벨 3짜리 잡템이
    /// 레벨 1짜리 핵심 아티팩트를 이긴다. 레어도를 근사치로 쓴다.
    ///
    /// 값들은 실측 근거가 없는 설계값이다. 추천이 이상하게 기울면 여기부터 의심한다.
    /// </summary>
    public static class Worth
    {
        /// <summary>같은 카테고리를 하나 더 모아 콤보가 발동할 때의 가치. 점수 단위로 레벨 2에 해당한다.</summary>
        public const double ComboThreshold = 2.0;

        /// <summary>아직 임계값에 못 미치지만 한 걸음 다가가는 가치.</summary>
        public const double ComboProgress = 0.25;

        /// <summary>
        /// 전체 피해 보너스 1점을 점수(레벨) 단위로 환산하는 값. 조화의 수정처럼 효과가 이웃에
        /// 달린 아티팩트를 점수에 넣으려면 다른 단위를 옮겨 와야 한다.
        ///
        /// 이 값도 아직 실측 근거가 없다. `--measure` 가 콤보와 함께 재는 대상이다.
        /// </summary>
        public const double DamageBonus = 0.05;

        public static double OfRarity(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => 1.1,
            Rarity.Rare => 1.25,
            Rarity.Legend => 1.45,
            Rarity.Eternal => 1.7,
            _ => 1.0,
        };

        /// <summary>
        /// 지금 개수에서 카테고리 하나를 더 모으는 것의 가치. <paramref name="goal"/>은 그때
        /// 노리게 되는 임계값이다. 임계값을 이미 다 넘겼으면 0.
        /// </summary>
        public static double OfComboStep(ComboDefinition combo, int currentCount, out bool completes, out int goal)
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
