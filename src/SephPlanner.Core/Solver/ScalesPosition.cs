using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 가방의 왼편·오른편에 따라 주는 것이 달라지는 아티팩트. 이미 놓여 있으면 그 쪽을 지킨다 -
    /// 반대쪽으로 옮기면 받던 속성이 통째로 바뀌는데, 우리는 좌우 중 어느 쪽이 이 빌드에 나은지를
    /// 견주지 못하므로 멋대로 뒤집지 않는다. 새로 들어와 게임이 넣어 준 자리 그대로인 대립의 천칭만은
    /// 사용자가 고른 편이 아니므로 우선 콤보를 따른다(<see cref="PreferredLeft"/>).
    /// </summary>
    public static class ScalesPosition
    {
        /// <summary>
        /// 좌우를 가르는 아티팩트. 게임에서 <c>XIdx &lt;= 2</c>로 편을 가르는 클래스가 이 둘뿐이다
        /// (1.0.31, <c>Charm_FireIce.AddStat</c>·<c>Charm_FireIceWeapon</c>).
        /// </summary>
        public static bool IsSideBound(CharmDefinition definition) =>
            definition.Behavior == "Charm_FireIce" || definition.Behavior == "Charm_FireIceWeapon";

        // 게임 Charm_FireIce.AddStat의 XIdx <= 2 판정이다. 가방 너비의 절반이 아니다.
        public static bool IsLeft(GridPos position) => position.X <= 2;

        /// <summary>
        /// 새로 들어오면 우선 콤보로 편을 고르는 아티팩트. 대립의 천칭뿐이다 - 영원의 식은 편에 따라
        /// 바뀌는 것이 속성 피해가 아니라 무기 연동이라 콤보로 고를 수 없다.
        /// </summary>
        public static bool FollowsPriority(CharmDefinition definition) => FollowsPriority(definition.Behavior);

        public static bool FollowsPriority(string behavior) => behavior == "Charm_FireIce";

        /// <summary>
        /// 새로 들어온 대립의 천칭이 갈 편. 게임은 왼편에서 화염, 오른편에서 얼음 피해를 크게 준다. 우선
        /// 콤보에 잉걸불과 빙하 중 하나만 있으면 그쪽이고, 아니면 없다.
        /// </summary>
        public static bool? PreferredLeft(CharmDefinition definition, ICollection<string> priorityCategories)
        {
            if (!FollowsPriority(definition)) return null;
            var ember = priorityCategories.Contains("EMBER");
            if (ember == priorityCategories.Contains("GLACIER")) return null;
            return ember;
        }

        internal static bool Required(PlacementProblem problem, CharmSlot charm) =>
            !charm.IsFiller && IsSideBound(charm.Definition) &&
            (charm.PreferredLeft.HasValue || problem.CurrentCharms.ContainsKey(charm.InstanceId));

        internal static bool Accepts(PlacementProblem problem, CharmSlot charm, GridPos position) =>
            !Required(problem, charm) || IsLeft(position) == Left(problem, charm);

        internal static bool Left(PlacementProblem problem, CharmSlot charm) =>
            charm.PreferredLeft ?? IsLeft(problem.CurrentCharms[charm.InstanceId]);
    }
}
