using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 가방의 왼편·오른편에 따라 주는 것이 달라지는 아티팩트. 이미 놓여 있으면 그 쪽을 지킨다 -
    /// 반대쪽으로 옮기면 받던 속성이 통째로 바뀌는데, 우리는 좌우 중 어느 쪽이 이 빌드에 나은지를
    /// 견주지 못하므로 멋대로 뒤집지 않는다.
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

        internal static bool Required(PlacementProblem problem, CharmSlot charm) =>
            !charm.IsFiller && IsSideBound(charm.Definition) && problem.CurrentCharms.ContainsKey(charm.InstanceId);

        internal static bool Accepts(PlacementProblem problem, CharmSlot charm, GridPos position) =>
            !Required(problem, charm) || IsLeft(position) == IsLeft(problem.CurrentCharms[charm.InstanceId]);
    }
}
