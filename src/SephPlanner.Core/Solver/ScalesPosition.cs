using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 가방의 왼편·오른편에 따라 주는 것이 달라지는 아티팩트. 이미 놓여 있으면 그 쪽을 지킨다 -
    /// 반대쪽으로 옮기면 받던 속성이 통째로 바뀌는데, 우리는 좌우 중 어느 쪽이 이 빌드에 나은지를
    /// 견주지 못하므로 멋대로 뒤집지 않는다. 새로 들어와 게임이 넣어 준 자리 그대로인 대립의 천칭만은
    /// 사용자가 고른 편이 아니므로 우선 콤보를 따른다(<see cref="PreferredLeft"/>). 나머지는 우선 콤보와 반대편에
    /// 있으면 옮기지 않고 알리기만 한다 - 한 번 옮겨 주면 그다음부터 그 편을 지킨다.
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

        /// <summary>새로 들어온 대립의 천칭이 갈 편(<see cref="PriorityLeft"/>).</summary>
        public static bool? PreferredLeft(CharmDefinition definition, ICollection<string> priorityCategories) =>
            FollowsPriority(definition) ? PriorityLeft(definition, priorityCategories) : null;

        /// <summary>
        /// 우선 콤보에 맞는 편. 참이면 왼편이다. 두 아티팩트 모두 왼편이 불, 오른편이 얼음 쪽이다 - 천칭은 왼편에서
        /// 화염, 오른편에서 얼음 피해를 크게 주고(<c>Charm_FireIce.AddStat</c>), 영원의 식은 왼편에서 서리 유물을
        /// 화염 피해로, 오른편에서 화염검을 얼음 속성으로 바꾼다(<c>FROSTRELICFLAME</c>·<c>FLAMESWORDFROST</c>).
        /// 잉걸불과 빙하 중 하나만 있으면 그 속성 쪽이다. 그것으로 못 가르면 영원의 식은 바뀌는 콤보로 가른다 -
        /// 서리만 있으면 서리 유물을 얼음으로 두는 오른편, 화염검만 있으면 화염검을 불로 두는 왼편이다.
        /// </summary>
        public static bool? PriorityLeft(CharmDefinition definition, ICollection<string> priorityCategories)
        {
            if (!IsSideBound(definition)) return null;
            var element = Either(priorityCategories, "EMBER", "GLACIER");
            if (element.HasValue || definition.Behavior != "Charm_FireIceWeapon") return element;
            return Either(priorityCategories, "FLAMESWORD", "FROST");
        }

        private static bool? Either(ICollection<string> priorityCategories, string left, string right)
        {
            var isLeft = priorityCategories.Contains(left);
            return isLeft == priorityCategories.Contains(right) ? null : isLeft;
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
