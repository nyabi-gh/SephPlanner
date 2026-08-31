using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 아티팩트의 상대 가치. 모든 아티팩트의 활성 1점을 같게 치면 레벨 3짜리 잡템이
    /// 레벨 1짜리 핵심 아티팩트를 이긴다. 레어도를 근사치로 쓴다.
    ///
    /// 레어도 배수는 아직 설계값이다(무엇이 센 아티팩트인지는 게임 데이터로 답이 나오지 않는다 —
    /// docs/ROADMAP.md 의 보류 항목). 콤보와 피해 환산값은 <c>--measure</c> 로 실측했다.
    /// </summary>
    public static class Worth
    {
        /// <summary>
        /// 같은 카테고리를 하나 더 모아 콤보가 발동할 때의 가치. 레벨 단위다.
        ///
        /// 실측값이다. 콤보가 임계값에서 주는 능력치를, 같은 능력치를 주는 아티팩트의 레벨당
        /// 증가분으로 나눠 레벨로 환산한 뒤 임계값 63건의 중앙값을 썼다(못 옮긴 11건 제외).
        /// 방법과 결과는 docs/RESEARCH.md 의 "콤보 가중치" 절.
        /// </summary>
        public const double ComboThreshold = 3.4;

        /// <summary>
        /// 아직 임계값에 못 미치지만 한 걸음 다가가는 가치.
        ///
        /// 이쪽은 실측이 아니다. 못 채운 콤보의 값어치는 그 판에서 결국 채우게 되느냐에 달려 있어
        /// 정적 데이터로는 답이 안 나온다. 그래서 <see cref="ComboThreshold"/>에 대한 비율
        /// (1/8)만 예전 그대로 두고 크기만 함께 옮겼다.
        /// </summary>
        public const double ComboProgress = 0.43;

        /// <summary>
        /// 전체 피해 보너스(<c>ECustomStat.AllDamageBonus</c>) 1점을 점수(레벨) 단위로 환산하는 값.
        /// 조화의 수정처럼 효과가 이웃에 달린 아티팩트를 점수에 넣으려면 단위를 옮겨 와야 한다.
        ///
        /// 실측값이다. 게임의 <c>StatusInstance_FinalDamage</c>가 조화의 수정과 똑같은 커스텀
        /// 능력치를 더하므로 `FINAL_DAMAGE`가 그대로 다리가 된다. 측정된 레벨당 증가분이 2.5라
        /// 1점은 레벨 0.4 값어치다.
        /// </summary>
        public const double DamageBonus = 0.4;

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
