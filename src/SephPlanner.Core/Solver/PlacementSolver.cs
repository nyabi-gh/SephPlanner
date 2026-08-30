using System;
using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 석판과 아티팩트를 격자에 배치해 점수를 최대화한다.
    ///
    /// 두 단계로 나눈다. 석판 배치는 조합 탐색이라 빔 서치로 후보를 좁히고, 석판이 고정되면
    /// 아티팩트 배치는 배정 문제가 되므로 헝가리안으로 정확히 푼다. 조건 판정이 배치에 의존하고
    /// 배치가 다시 조건에 의존하므로 몇 번 되풀이해 수렴시킨 뒤, 마지막 점수는 수렴한 배치로
    /// 다시 계산한다. 그래서 보고되는 점수는 항상 실제 배치의 점수다.
    /// </summary>
    public static class PlacementSolver
    {
        public static Arrangement Solve(PlacementProblem problem, SolverOptions? options = null)
        {
            options ??= new SolverOptions();

            var cells = Enumerable.Range(0, problem.Grid.Storage)
                                  .Select(problem.Grid.ToPosition)
                                  .ToList();

            var candidates = SearchTabletLayouts(problem, cells, options);

            Arrangement? best = null;
            foreach (var layout in candidates.Take(options.ExactCandidates))
            {
                var arrangement = Evaluate(problem, cells, layout, options);
                if (best == null || arrangement.Score > best.Score) best = arrangement;
            }
            return best ?? Evaluate(problem, cells, new List<TabletPlacement>(), options);
        }

        private static List<List<TabletPlacement>> SearchTabletLayouts(
            PlacementProblem problem, List<GridPos> cells, SolverOptions options)
        {
            var beam = new List<List<TabletPlacement>> { new List<TabletPlacement>() };

            foreach (var slot in problem.Tablets)
            {
                var rotations = slot.Definition.IsRotatable ? 4 : 1;
                var expanded = new List<(List<TabletPlacement> Layout, double Score)>();

                foreach (var layout in beam)
                {
                    var taken = new HashSet<GridPos>(layout.Select(p => p.Position));

                    foreach (var cell in cells)
                    {
                        if (taken.Contains(cell)) continue;

                        for (var rotation = 0; rotation < rotations; rotation++)
                        {
                            var next = new List<TabletPlacement>(layout) { slot.At(cell, rotation) };
                            expanded.Add((next, Estimate(problem, cells, next)));
                        }
                    }
                }

                if (expanded.Count == 0) break;

                beam = expanded.OrderByDescending(entry => entry.Score)
                               .Take(options.BeamWidth)
                               .Select(entry => entry.Layout)
                               .ToList();
            }
            return beam;
        }

        /// <summary>
        /// 아티팩트를 실제로 배정하지 않고 매기는 값. 빔을 좁히는 용도이므로 정확할 필요는 없고
        /// 유망한 배치를 위로 올리기만 하면 된다.
        /// </summary>
        private static double Estimate(
            PlacementProblem problem, List<GridPos> cells, List<TabletPlacement> layout)
        {
            var occupancy = OptimisticOccupancy(cells, layout, problem);
            var result = TabletSimulator.Run(layout, occupancy, problem.Grid);

            var taken = new HashSet<GridPos>(layout.Select(p => p.Position));
            var levelCap = problem.Charms.Count > 0
                ? problem.Charms.Max(c => c.Definition.MaxLevel)
                : 5;

            var levels = new List<int>();
            foreach (var cell in cells)
            {
                if (taken.Contains(cell)) continue;
                if (result.IsDisabled(cell)) continue;

                var level = EffectiveLevel(result, cell, 0);
                if (level < 0) continue;
                levels.Add(Math.Min(level, levelCap));
            }

            levels.Sort();
            levels.Reverse();
            return levels.Take(problem.Charms.Count).Sum();
        }

        private static Arrangement Evaluate(
            PlacementProblem problem, List<GridPos> cells, List<TabletPlacement> layout, SolverOptions options)
        {
            var occupancy = OptimisticOccupancy(cells, layout, problem);
            var taken = new HashSet<GridPos>(layout.Select(p => p.Position));
            var free = cells.Where(cell => !taken.Contains(cell)).ToList();

            Dictionary<int, GridPos> positions = new Dictionary<int, GridPos>();
            SimulationResult result = TabletSimulator.Run(layout, occupancy, problem.Grid);

            for (var iteration = 0; iteration < options.FixpointIterations; iteration++)
            {
                result = TabletSimulator.Run(layout, occupancy, problem.Grid);
                var next = Assign(problem, free, result, occupancy);
                if (SamePositions(positions, next)) break;

                positions = next;
                occupancy = OccupancyFrom(layout, positions, problem);
            }

            return Describe(problem, layout, positions, occupancy, result);
        }

        private static Dictionary<int, GridPos> Assign(
            PlacementProblem problem, List<GridPos> free, SimulationResult result, GridOccupancy occupancy)
        {
            var positions = new Dictionary<int, GridPos>();
            if (problem.Charms.Count == 0 || free.Count == 0) return positions;

            var charmsAreRows = problem.Charms.Count <= free.Count;
            var rows = charmsAreRows ? problem.Charms.Count : free.Count;
            var columns = charmsAreRows ? free.Count : problem.Charms.Count;
            var cost = new double[rows, columns];

            for (var charmIndex = 0; charmIndex < problem.Charms.Count; charmIndex++)
            {
                for (var cellIndex = 0; cellIndex < free.Count; cellIndex++)
                {
                    // 헝가리안은 비용을 최소화하므로 점수를 뒤집어 넣는다.
                    var value = -Value(problem.Charms[charmIndex], free[cellIndex], result, problem.Grid, occupancy);
                    if (charmsAreRows) cost[charmIndex, cellIndex] = value;
                    else cost[cellIndex, charmIndex] = value;
                }
            }

            var assignment = HungarianAssignment.Solve(cost, out _);
            for (var row = 0; row < assignment.Length; row++)
            {
                if (assignment[row] < 0) continue;

                var charmIndex = charmsAreRows ? row : assignment[row];
                var cellIndex = charmsAreRows ? assignment[row] : row;
                positions[problem.Charms[charmIndex].InstanceId] = free[cellIndex];
            }
            return positions;
        }

        private static double Value(
            CharmSlot charm, GridPos cell, SimulationResult result, GridSpec grid, GridOccupancy occupancy)
        {
            if (result.IsDisabled(cell)) return 0;

            var level = EffectiveLevel(result, cell, charm.Enchant);
            if (level < 0) return 0;

            var ignoresCriteria = result.IgnoreCriteria.TryGetValue(cell, out var ignore) && ignore > 0;
            if (!ignoresCriteria)
            {
                var kind = CharmCriteria.FromTypeName(charm.Definition.CriteriaType);
                if (!CharmCriteria.IsSatisfied(kind, cell, grid, occupancy)) return 0;
            }

            return charm.Weight * Math.Min(charm.Definition.MaxLevel, level);
        }

        private static int EffectiveLevel(SimulationResult result, GridPos cell, int enchant)
        {
            var level = result.LevelAt(cell) + enchant;
            if (result.MultiplyLevel.TryGetValue(cell, out var multiplier) && multiplier != 0)
                level *= multiplier;
            return level;
        }

        private static GridOccupancy OptimisticOccupancy(
            List<GridPos> cells, List<TabletPlacement> layout, PlacementProblem problem)
        {
            // 아직 아티팩트를 배정하기 전이라 빈 칸이 모두 찬다고 본다. 수렴 단계에서 실제 배치로 대체된다.
            var occupancy = new GridOccupancy();
            var taken = new HashSet<GridPos>(layout.Select(p => p.Position));
            var anyMagic = problem.Charms.Any(c => c.Definition.IsMagic);

            foreach (var cell in cells)
                occupancy.AddItem(cell, !taken.Contains(cell), anyMagic && !taken.Contains(cell));

            return occupancy;
        }

        private static GridOccupancy OccupancyFrom(
            List<TabletPlacement> layout, Dictionary<int, GridPos> positions, PlacementProblem problem)
        {
            var occupancy = new GridOccupancy();
            foreach (var placement in layout) occupancy.AddItem(placement.Position, false);

            foreach (var charm in problem.Charms)
            {
                if (!positions.TryGetValue(charm.InstanceId, out var position)) continue;
                occupancy.AddItem(position, true, charm.Definition.IsMagic);
            }
            return occupancy;
        }

        private static bool SamePositions(Dictionary<int, GridPos> left, Dictionary<int, GridPos> right)
        {
            if (left.Count != right.Count) return false;
            foreach (var pair in left)
                if (!right.TryGetValue(pair.Key, out var position) || position != pair.Value) return false;
            return true;
        }

        private static Arrangement Describe(
            PlacementProblem problem, List<TabletPlacement> layout, Dictionary<int, GridPos> positions,
            GridOccupancy occupancy, SimulationResult result)
        {
            var arrangement = new Arrangement();
            arrangement.Tablets.AddRange(layout);

            foreach (var charm in problem.Charms)
            {
                if (!positions.TryGetValue(charm.InstanceId, out var position))
                {
                    arrangement.InactiveCharms.Add(charm.InstanceId);
                    continue;
                }

                arrangement.CharmPositions[charm.InstanceId] = position;
                var value = Value(charm, position, result, problem.Grid, occupancy);
                arrangement.Score += value;
                if (value <= 0) arrangement.InactiveCharms.Add(charm.InstanceId);

                arrangement.Levels[position] = EffectiveLevel(result, position, charm.Enchant);
            }
            return arrangement;
        }
    }
}
