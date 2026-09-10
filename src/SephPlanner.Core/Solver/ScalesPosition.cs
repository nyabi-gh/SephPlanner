using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    public static class ScalesPosition
    {
        // 게임 Charm_FireIce.AddStat의 XIdx <= 2 판정이다. 가방 너비의 절반이 아니다.
        public static bool IsLeft(GridPos position) => position.X <= 2;

        internal static bool Required(PlacementProblem problem, CharmSlot charm) =>
            !charm.IsFiller && charm.Definition.Behavior == "Charm_FireIce" && problem.CurrentCharms.ContainsKey(charm.InstanceId);

        internal static bool Accepts(PlacementProblem problem, CharmSlot charm, GridPos position) =>
            !Required(problem, charm) || IsLeft(position) == IsLeft(problem.CurrentCharms[charm.InstanceId]);
    }
}
