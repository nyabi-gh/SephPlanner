using System;
using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    internal static class ContextStatWorth
    {
        internal static double Value(PlacementProblem problem, CharmSlot charm, GridPos cell, int level,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors, bool estimate = false,
            SimulationResult? result = null, GridOccupancy? occupancy = null)
        {
            if (charm.Worth.Source == CharmWorthSource.Curated) return charm.Worth.WeightedAt(level, charm.Weight);
            if (estimate && charm.Definition.ContextStats.Any(bonus => bonus.Source == StatCountSource.RowCategory))
            {
                var best = double.NegativeInfinity;
                for (var row = 0; row < problem.Grid.Height; row++)
                    if (problem.Grid.Contains(new GridPos(0, row)))
                        best = Math.Max(best, Value(problem, charm, new GridPos(0, row), level, null));
                return double.IsNegativeInfinity(best) ? 0 : best;
            }
            double benefit = At(charm.Definition.StatBenefitByLevel, level);
            double penalty = At(charm.Definition.StatPenaltyByLevel, level);
            var support = BuildStatWorth.Value(problem, charm, level, result, occupancy, neighbors) - charm.Worth.WeightedAt(level, charm.Weight);
            var complete = charm.Definition.StatWorthCoverageKnown && charm.Definition.StatWorthUnconverted.Count == 0;
            foreach (var bonus in charm.Definition.ContextStats)
            {
                if (!bonus.WorthPerUnit.HasValue) { complete = false; continue; }
                var count = Count(problem, charm, cell, bonus, neighbors, estimate);
                var value = At(bonus.AmountByLevel, level) * bonus.WorthPerUnit.Value * count;
                benefit += Math.Max(0, value);
                penalty += Math.Min(0, value);
                if (value > 0)
                    support += value * (BuildStatWorth.Weight(problem, bonus.StatusId, charm.Weight, result, occupancy, neighbors) - charm.Weight);
            }
            // 미환산 능력치가 있을 때만 기존 추정 하한을 보충한다. 측정된 손해는 가중하지 않는다.
            if (!complete) benefit += Math.Max(0, charm.Worth.At(level) - benefit - penalty);
            return benefit * charm.Weight + penalty + support;
        }

        private static int Count(PlacementProblem problem, CharmSlot charm, GridPos cell, ContextStatBonus bonus,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors, bool estimate)
        {
            if (bonus.Source == StatCountSource.StoneTablets) return problem.Tablets.Count;
            if (bonus.Source == StatCountSource.RowCategory)
                return charm.Definition.LineCategories.Count > 0 &&
                       (estimate || PositionalWorth.LineCategory(charm.Definition, cell) == bonus.Category) ? 1 : 0;
            if (neighbors is null || estimate)
                return Math.Min(bonus.SlotCount, problem.Charms.Count(item => !item.IsFiller));
            var count = 0;
            for (var index = 0; index < Math.Min(bonus.SlotCount, problem.Grid.Storage); index++)
            {
                var at = problem.Grid.ToPosition(index);
                if (at == cell) { count++; continue; }
                if (!neighbors.TryGetValue(at, out var item)) continue;
                if (item == charm && !neighbors.TryGetValue(cell, out item)) continue;
                if (item == charm || item.IsFiller) continue;
                count++;
            }
            return count;
        }

        private static double At(List<double> table, int level) =>
            table.Count == 0 ? 0 : table[Math.Min(Math.Max(0, level), table.Count - 1)];
    }
}
