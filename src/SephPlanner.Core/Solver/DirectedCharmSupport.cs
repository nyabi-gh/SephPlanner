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

        internal static double Recovery(CharmSlot helper, int level)
        {
            var table = helper.Definition.MagicCooldownSupport?.RecoveryByLevel;
            return table is null || table.Count == 0 ? 0 :
                table[Math.Min(Math.Max(0, Math.Min(helper.Definition.MaxLevel, level)), table.Count - 1)];
        }

        internal static bool TryTarget(
            CharmSlot helper, GridPos cell, SimulationResult result, GridSpec grid, GridOccupancy occupancy,
            IReadOnlyDictionary<GridPos, CharmSlot>? neighbors, out CharmSlot target, out GridPos targetCell)
        {
            target = helper;
            targetCell = cell;
            var support = helper.Definition.MagicCooldownSupport;
            if (support is null || support.RecoveryByLevel.Count == 0 || neighbors is null) return false;
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
            return Benefit(helper, level, target.Worth.At(targetLevel));
        }

        internal static double Estimate(PlacementProblem problem, CharmSlot helper, int level)
        {
            var best = 0.0;
            var found = false;
            foreach (var target in problem.Charms)
            {
                if (target == helper || !IsTarget(target)) continue;
                found = true;
                for (var targetLevel = 0; targetLevel <= target.Definition.MaxLevel; targetLevel++)
                    best = Math.Max(best, target.Worth.At(targetLevel));
            }
            return found ? Benefit(helper, level, best) : 0;
        }

        private static double Benefit(CharmSlot helper, int level, double targetWorth)
        {
            if (helper.Worth.Source == CharmWorthSource.Curated)
                return helper.Worth.WeightedAt(Math.Min(helper.Definition.MaxLevel, level), helper.Weight);
            // 게임은 기본 쿨다운을 (100 + 회복 속도)/100으로 나눈다. 여기서는 추가 버프·마나·시전
            // 빈도를 모르는 상태의 환산 추정치로, 기본 속도에서 증가하는 사용 횟수 비율을 쓴다.
            return CharmWorth.ApplyWeight(Math.Max(0, targetWorth) * Recovery(helper, level) / 100, helper.Weight);
        }
    }
}
