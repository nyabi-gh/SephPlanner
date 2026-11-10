using System.Reflection;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 목표 횟수를 채우면 다른 아티팩트로 바뀌는 성장. 게임에는 두 갈래가 있다 - <c>Charm_GrowthStatusInstance</c> 와,
    /// 처치 수로 자라는 <c>Charm_SweepRange</c>(버려진 금반지 1111 → 1112). 둘 다 HUD 에 <c>n/목표</c> 를 띄운다.
    /// </summary>
    internal static class GrowthQuest
    {
        private static readonly FieldInfo GrowthCounter =
            GameBinding.Field(typeof(Charm_GrowthStatusInstance), "questCounter");

        private static readonly FieldInfo SweepCounter =
            GameBinding.Field(typeof(Charm_SweepRange), "killCounter");

        public static int Goal(Charm_Basic charm)
        {
            if (charm is Charm_GrowthStatusInstance growth) return growth.hasGrowthQuest ? growth.growthQuestGoal : 0;
            if (charm is Charm_SweepRange sweep) return sweep.hasSweepQuest ? sweep.sweepQuestKillCount : 0;
            return 0;
        }

        public static int Reward(Charm_Basic charm)
        {
            if (charm is Charm_GrowthStatusInstance growth) return growth.reward != null ? growth.reward.id : 0;
            if (charm is Charm_SweepRange sweep) return sweep.reward != null ? sweep.reward.id : 0;
            return 0;
        }

        /// <summary>서버의 원본 횟수. 참가자 자리에서 읽으면 초기값이 나오므로 서버에서만 부른다.</summary>
        public static int? Counter(Charm_Basic charm)
        {
            var field = charm is Charm_GrowthStatusInstance ? GrowthCounter : charm is Charm_SweepRange ? SweepCounter : null;
            return field?.GetValue(charm) is int counter ? counter : (int?)null;
        }
    }
}
