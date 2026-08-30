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
        public static double OfRarity(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => 1.1,
            Rarity.Rare => 1.25,
            Rarity.Legend => 1.45,
            Rarity.Eternal => 1.7,
            _ => 1.0,
        };
    }
}
