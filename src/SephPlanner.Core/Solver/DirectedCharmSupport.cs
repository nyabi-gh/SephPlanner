using System;
using System.Collections.Generic;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    internal static class DirectedCharmSupport
    {
        internal static bool IsTarget(CharmSlot target) =>
            target.Definition.IsMagic && !target.IsFiller && !target.IsDormant;

        internal static double Amount(CharmSlot helper, int level)
        {
            var table = helper.Definition.MagicSupport?.AmountByLevel;
            return table is null || table.Count == 0 ? 0 :
                table[Math.Min(Math.Max(0, Math.Min(helper.Definition.MaxLevel, level)), table.Count - 1)];
        }

        internal static bool TryTarget(
            CharmSlot helper, GridPos cell, SimulationResult result, GridSpec grid, GridOccupancy occupancy,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors, out CharmSlot target, out GridPos targetCell)
        {
            target = helper;
            targetCell = cell;
            var support = helper.Definition.MagicSupport;
            if (support is null || support.AmountByLevel.Count == 0 || neighbors is null) return false;
            targetCell = cell.Offset(support.OffsetX, support.OffsetY);
            if (!grid.Contains(targetCell) || !neighbors.TryGetValue(targetCell, out var found)) return false;
            // 배정 중에는 이전 자리에 선 자신 대신 교환될 아티팩트를 본다. 최종 채점은 실제 점유를 쓴다.
            if (found == helper && !neighbors.TryGetValue(cell, out found)) return false;
            if (found == helper || !IsTarget(found) ||
                PlacementSolver.Reason(found, targetCell, result, grid, occupancy) != CharmInactiveReason.None) return false;
            target = found;
            return true;
        }

        internal static double Value(
            PlacementProblem problem, CharmSlot helper, GridPos cell, int level, SimulationResult result,
            GridOccupancy occupancy, IReadOnlyDictionary<GridPos, CharmSlot>? neighbors)
        {
            if (neighbors is null) return Estimate(problem, helper, level);
            if (!TryTarget(helper, cell, result, problem.Grid, occupancy, neighbors, out var target, out var targetCell)) return 0;
            var targetLevel = Math.Min(target.Definition.MaxLevel, result.EffectiveLevel(targetCell, target.Enchant));
            return Benefit(helper, level, target, targetLevel);
        }

        internal static double Estimate(PlacementProblem problem, CharmSlot helper, int level)
        {
            var best = double.NegativeInfinity;
            var found = false;
            foreach (var target in problem.Charms)
            {
                if (target == helper || !IsTarget(target)) continue;
                found = true;
                for (var targetLevel = 0; targetLevel <= target.Definition.MaxLevel; targetLevel++)
                    best = Math.Max(best, Benefit(helper, level, target, targetLevel));
            }
            return found ? best : 0;
        }

        private static double Benefit(CharmSlot helper, int level, CharmSlot target, int targetLevel)
        {
            if (helper.Worth.Source == CharmWorthSource.Curated)
                return helper.Worth.WeightedAt(Math.Min(helper.Definition.MaxLevel, level), helper.Weight);
            var amount = Amount(helper, level);
            var ratio = amount / 100;
            if (helper.Definition.MagicSupport!.Effect == MagicSupportEffect.ManaCostReduction)
            {
                var costs = target.Definition.MagicCostByLevel;
                if (costs.Count == 0) return 0;
                var cost = costs[Math.Min(Math.Max(0, targetLevel), costs.Count - 1)];
                if (cost <= 0) return 0;
                var adjusted = (float)cost + (float)cost * (-(float)amount / 100f);
                var reduced = amount >= 100 ? 0 : Math.Round(adjusted, MidpointRounding.ToEven);
                ratio = (cost - reduced) / cost;
            }
            // 회복은 기본 속도 대비 사용 횟수 증가, 비용 감소는 기본 마나 비용 대비 절약률로 환산한
            // 추정이다. 추가 버프·비용 변경·실제 시전 빈도를 포함한 전투 효율은 아니다.
            return CharmWorth.ApplyWeight(Math.Max(0, target.Worth.At(targetLevel)) * ratio, helper.Weight);
        }
    }
}
