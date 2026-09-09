namespace SephPlanner.Core.Solver
{
    internal static class ActivationPolicy
    {
        internal static bool AllowsTransition(Arrangement current, Arrangement proposed)
        {
            foreach (var id in proposed.UnpreservedCharms)
                if (current.CharmPositions.ContainsKey(id) && !current.UnpreservedCharms.Contains(id)) return false;
            return true;
        }
    }
}
