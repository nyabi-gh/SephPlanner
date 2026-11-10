namespace SephPlanner.Core.Solver
{
    internal static class ActivationPolicy
    {
        internal static bool AllowsTransition(Arrangement current, Arrangement proposed) =>
            proposed.WrongSideCharms.Count == 0 && proposed.CombinedCharms.Count == 0 && DisablesNothingNew(current, proposed);

        /// <summary>
        /// 끄기 허용 없이 새로 꺼지는 아티팩트가 없는가. <see cref="AllowsTransition"/> 의 다른 거절(천칭, 합쳐짐)과 갈라
        /// 두는 것은 플레이어에게 사유를 따로 말하기 때문이다.
        /// </summary>
        internal static bool DisablesNothingNew(Arrangement current, Arrangement proposed)
        {
            if (proposed.UnapprovedDeactivations.Count > 0) return false;
            foreach (var id in proposed.UnpreservedCharms)
                if (current.CharmPositions.ContainsKey(id) && !current.UnpreservedCharms.Contains(id)) return false;
            return true;
        }
    }
}
