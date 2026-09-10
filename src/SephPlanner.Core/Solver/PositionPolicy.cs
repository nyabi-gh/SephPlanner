using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    public static class PositionPolicy
    {
        internal static HorizontalSide Side(PlacementProblem problem, CharmSlot charm) => charm.IsFiller ? HorizontalSide.Automatic :
            charm.Definition.Behavior == "Charm_FireIce" ? problem.ScalesSide :
            charm.Definition.Combat.FireIcePosition ? problem.EternalSide : HorizontalSide.Automatic;

        internal static bool Required(PlacementProblem problem, CharmSlot charm) => Side(problem, charm) != HorizontalSide.Automatic;

        internal static bool Accepts(PlacementProblem problem, CharmSlot charm, GridPos position) =>
            !Required(problem, charm) || HorizontalStatBonus.IsLeft(position) == (Side(problem, charm) == HorizontalSide.Left);

        internal static bool Allows(Arrangement arrangement) => arrangement.UnpositionedCharms.Count == 0;

        public static string Label(HorizontalSide side) => side == HorizontalSide.Left ? "불 우선(왼쪽)" :
            side == HorizontalSide.Right ? "얼음 우선(오른쪽)" : "자동(DPS)";

        public static string EternalLabel(HorizontalSide side) => side == HorizontalSide.Left ? "왼쪽 · 얼음 무구 → 화염" :
            side == HorizontalSide.Right ? "오른쪽 · 화염검 → 얼음" : "자동(DPS)";

        internal static string Label(PlacementProblem problem, CharmSlot charm) => charm.Definition.Combat.FireIcePosition
            ? EternalLabel(Side(problem, charm)) : Label(Side(problem, charm));
    }
}
