using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    public static class PositionPolicy
    {
        internal static bool Required(PlacementProblem problem, CharmSlot charm) =>
            problem.ScalesSide != HorizontalSide.Automatic && !charm.IsFiller && charm.Definition.Behavior == "Charm_FireIce";

        internal static bool Accepts(PlacementProblem problem, CharmSlot charm, GridPos position) =>
            !Required(problem, charm) || HorizontalStatBonus.IsLeft(position) == (problem.ScalesSide == HorizontalSide.Left);

        internal static bool Allows(Arrangement arrangement) => arrangement.UnpositionedCharms.Count == 0;

        public static string Label(HorizontalSide side) => side == HorizontalSide.Left ? "불 우선(왼쪽)" :
            side == HorizontalSide.Right ? "얼음 우선(오른쪽)" : "자동(DPS)";
    }
}
