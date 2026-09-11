using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 콤보와 피해 보너스를 아티팩트 레벨 단위로 옮기는 환산값. 셋 다 <c>--measure</c> 로 실측했다.
    ///
    /// 아티팩트 하나하나의 값어치는 여기가 아니라 <see cref="CharmWorth"/>가 답한다.
    /// </summary>
    public static class Worth
    {
        /// <summary>
        /// 같은 카테고리를 하나 더 모아 콤보가 발동할 때의 가치. 레벨 단위다.
        ///
        /// 실측값이다. 콤보가 임계값에서 주는 능력치를 <see cref="StatExchange"/>로 레벨로
        /// 환산한 뒤 임계값 63건의 중앙값을 썼다(못 옮긴 11건 제외).
        /// 방법과 결과는 docs/RESEARCH.md 의 "콤보 가중치" 절.
        /// </summary>
        public static double ComboThreshold => WorthScale.Default.ComboThreshold;

        /// <summary>
        /// 아직 임계값에 못 미치지만 한 걸음 다가가는 가치.
        ///
        /// 이쪽은 실측이 아니다. 못 채운 콤보의 값어치는 그 판에서 결국 채우게 되느냐에 달려 있어
        /// 정적 데이터로는 답이 안 나온다. 그래서 <see cref="ComboThreshold"/>에 대한 비율
        /// (1/8)만 예전 그대로 두고 크기만 함께 옮겼다.
        /// </summary>
        public static double ComboProgress => WorthScale.Default.ComboProgress;

        /// <summary>
        /// 전체 피해 보너스(<c>ECustomStat.AllDamageBonus</c>) 1점을 점수(레벨) 단위로 환산하는 값.
        /// 조화의 수정처럼 효과가 이웃에 달린 아티팩트를 점수에 넣으려면 단위를 옮겨 와야 한다.
        ///
        /// 실측값이다. 게임의 <c>StatusInstance_FinalDamage</c>가 조화의 수정과 똑같은 커스텀
        /// 능력치를 더하므로 `FINAL_DAMAGE`가 그대로 다리가 된다. 측정된 레벨당 증가분이 3.80이라
        /// 1점은 레벨 0.26 값어치다.
        /// </summary>
        public static double DamageBonus => WorthScale.Default.DamageBonus;

        /// <summary>
        /// 아무 근거가 없을 때 쓰는 마지막 어림값. 능력치를 주지 않아 잴 수 없고 손으로도 채우지
        /// 않은 아티팩트에만 쓰인다.
        ///
        /// **레어도는 세기의 대리값으로서 좋지 않다.** 잴 수 있는 아티팩트 110종을 재 보니
        /// 레벨 0의 값어치 중앙값이 일반 1.50, 고급 1.67, 희귀 2.00, 전설 1.47 로 순서가
        /// 서지 않았다. 같은 레어도 안의 폭(-1.55 ~ 20.00)이 레어도 사이의 차이보다 훨씬 크다.
        /// 그러니 이 배수는 "레어도가 높으면 조금 낫겠거니"라는 뜻일 뿐이고, 여기에 걸린
        /// 아티팩트를 줄여 가는 것이 <c>data/values/charms.json</c>을 채우는 목적이다.
        /// </summary>
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
        public static double OfComboStep(ComboDefinition combo, int currentCount, out bool completes, out int goal) =>
            WorthScale.Default.OfComboStep(combo, currentCount, out completes, out goal);
    }
}
