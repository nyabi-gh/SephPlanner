using System;
using System.Collections.Generic;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    internal static class BuildStatWorth
    {
        internal static double Value(PlacementProblem problem, CharmSlot charm, int level,
            SimulationResult? result = null, GridOccupancy? occupancy = null,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors = null)
        {
            var value = charm.Worth.WeightedAt(level, charm.Weight);
            if (charm.Worth.Source == CharmWorthSource.Curated) return value;
            foreach (var effect in charm.Definition.StatEffects)
            {
                if (!effect.WorthPerUnit.HasValue || effect.AmountByLevel.Count == 0) continue;
                var amount = effect.AmountByLevel[Math.Min(Math.Max(0, level), effect.AmountByLevel.Count - 1)];
                if (amount <= 0) continue;
                var weight = Weight(problem, effect.StatusId, charm.Weight, result, occupancy, neighbors);
                // 전투 사용률이 아닌 주력 선호의 전달이다. 중복 지정은 최댓값만 쓰고 손해는 그대로 둔다.
                value += amount * effect.WorthPerUnit.Value * (weight - charm.Weight);
            }
            return value;
        }

        internal static double Weight(PlacementProblem problem, string status, double ownWeight,
            SimulationResult? result, GridOccupancy? occupancy,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors)
        {
            var weight = ownWeight;
            if (ownWeight < 1 || status != "MAGIC_CRITICAL" && status != "MP_REGEN") return weight;
            foreach (var target in problem.Charms)
            {
                if (target.IsFiller || target.IsDormant || target.Weight <= Math.Max(1, weight)) continue;
                if (status == "MAGIC_CRITICAL" && !(target.Definition.UsesMagicCritical &&
                    (target.IsAttackable ?? target.Definition.IsAttackable))) continue;
                if (status == "MP_REGEN" && (!target.Definition.IsMagic ||
                    !target.Definition.MagicCostByLevel.Exists(amount => amount > 0))) continue;
                if (neighbors is not null && result is not null && occupancy is not null)
                {
                    var active = false;
                    foreach (var pair in neighbors)
                        if (pair.Value == target && PlacementSolver.Reason(target, pair.Key, result, problem.Grid, occupancy) ==
                            CharmInactiveReason.None)
                        {
                            var level = Math.Min(target.Definition.MaxLevel, result.EffectiveLevel(pair.Key, target.Enchant));
                            var costs = target.Definition.MagicCostByLevel;
                            active = status != "MP_REGEN" || costs[Math.Min(level, costs.Count - 1)] > 0;
                            break;
                        }
                    if (!active) continue;
                }
                weight = target.Weight;
            }
            return weight;
        }
    }
}
