using System.Collections.Generic;

namespace SephPlanner.Core.Planning
{
    /// <summary>
    /// 사용자가 고른 빌드 방향. 계산마다 새로 만들어 넘긴다(불변으로 취급).
    /// </summary>
    public sealed class PlanPreferences
    {
        public static readonly PlanPreferences None = new PlanPreferences();

        /// <summary>
        /// 밀고 있는 콤보 카테고리. 후보 추천에서 이 카테고리의 콤보 진행을 훨씬 크게 치고,
        /// 카테고리가 맞는 아티팩트를 위로 올린다. 배치 점수는 건드리지 않는다.
        /// </summary>
        public HashSet<string> PriorityCategories { get; set; } = new HashSet<string>();

        /// <summary>
        /// 강화 우선으로 지정한 아티팩트(엔티티 번호). 배치에서 가치를 <see cref="PinnedWeight"/>배로
        /// 쳐서 좋은 칸을 먼저 받는다. 점수도 그 기준으로 계산된다.
        /// </summary>
        public HashSet<int> PinnedCharms { get; set; } = new HashSet<int>();

        public const double PinnedWeight = 2.0;
    }
}
