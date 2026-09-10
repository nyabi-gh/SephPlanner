namespace SephPlanner.Core.Solver
{
    internal static class ActivationPolicy
    {
        internal static bool AllowsTransition(Arrangement current, Arrangement proposed)
        {
            if (proposed.UnapprovedDeactivations.Count > 0 || proposed.WrongSideCharms.Count > 0) return false;
            foreach (var id in proposed.UnpreservedCharms)
                if (current.CharmPositions.ContainsKey(id) && !current.UnpreservedCharms.Contains(id)) return false;
            return true;
        }
    }
}
