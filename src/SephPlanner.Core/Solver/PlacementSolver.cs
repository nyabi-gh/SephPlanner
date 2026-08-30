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
        private const double WastePenalty = 1e-4;

        /// <summary>
        /// 효과가 켜져 있는 아티팩트 하나의 값어치. 레벨은 세기를 더할 뿐이고 레벨 0 도 살아 있다.
        /// 이것이 없으면 꺼지는 자리(레벨 음수)와 레벨 0 자리가 똑같이 0점이라, 아티팩트를
        /// 꺼진 채로 두고도 최적이라고 하게 된다.
        /// </summary>
        private const double ActiveValue = 1;

        /// <summary>낭비 판단보다도 작게 두어, 정말 우열이 없을 때만 현 상태를 유지하도록 한다.</summary>
        private const double StabilityBonus = 1e-6;

        public static Arrangement Solve(PlacementProblem problem, SolverOptions? options = null)
        {
            options ??= new SolverOptions();

            var cells = Enumerable.Range(0, problem.Grid.Storage)
                                  .Select(problem.Grid.ToPosition)
                                  .ToList();

            var candidates = SearchTabletLayouts(problem, cells, options)
                .Take(options.ExactCandidates)
                .ToList();

            // 탐색이 현재 배치를 후보에서 떨어뜨리면, 이미 최적인 배치를 두고도 옮기라고 하게 된다.
            var asIs = CurrentLayout(problem);
            if (asIs != null) candidates.Insert(0, asIs);

            Arrangement? best = null;
            foreach (var layout in candidates)
            {
                var arrangement = Evaluate(problem, cells, layout, options);
                if (best == null || arrangement.Score > best.Score) best = arrangement;
            }
            return best ?? Evaluate(problem, cells, new List<TabletPlacement>(), options);
        }

        /// <summary>이미 정해진 배치를 같은 기준으로 채점한다. 현재 배치와 제안을 비교할 때 쓴다.</summary>
        public static Arrangement Score(
            PlacementProblem problem,
            IReadOnlyList<TabletPlacement> layout,
            IReadOnlyDictionary<int, GridPos> charmPositions)
        {
            var positions = new Dictionary<int, GridPos>(charmPositions.Count);
            foreach (var pair in charmPositions) positions[pair.Key] = pair.Value;

            var placements = new List<TabletPlacement>(layout);
            var occupancy = OccupancyFrom(placements, positions, problem);
            var result = TabletSimulator.Run(WithFixed(problem, placements), occupancy, problem.Grid, problem.FixedEffects);

            return Describe(problem, placements, positions, occupancy, result);
        }

        /// <summary>
        /// 효과를 계산할 때는 옮길 수 있는 석판과 고정된 각인을 함께 넣는다. 각인은 칸을 차지하지
        /// 않으므로 배치 후보에서 자리를 빼앗지는 않는다.
        /// </summary>
        private static IReadOnlyList<TabletPlacement> WithFixed(
            PlacementProblem problem, List<TabletPlacement> layout)
        {
            if (problem.FixedTablets.Count == 0) return layout;

            var all = new List<TabletPlacement>(layout.Count + problem.FixedTablets.Count);
            all.AddRange(layout);
            all.AddRange(problem.FixedTablets);
            return all;
        }

        private static List<TabletPlacement>? CurrentLayout(PlacementProblem problem)
        {
            if (problem.Tablets.Count == 0 || problem.CurrentTablets.Count < problem.Tablets.Count) return null;

            var layout = new List<TabletPlacement>(problem.Tablets.Count);
            var taken = new HashSet<GridPos>();

            foreach (var slot in problem.Tablets)
            {
                if (!problem.CurrentTablets.TryGetValue(slot.InstanceId, out var spot)) return null;
                if (!taken.Add(spot.Position)) return null;
                layout.Add(slot.At(spot.Position, spot.Rotation));
            }
            return layout;
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
            var result = TabletSimulator.Run(WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);

            var taken = new HashSet<GridPos>(layout.Select(p => p.Position));
            var scoring = problem.Charms.Where(c => !c.IsFiller && !c.IsDormant).ToList();
            var levelCap = scoring.Count > 0 ? scoring.Max(c => c.Definition.MaxLevel) : 5;

            var levels = new List<int>();
            foreach (var cell in cells)
            {
                if (taken.Contains(cell)) continue;
                if (result.IsDisabled(cell)) continue;

                var level = result.EffectiveLevel(cell, 0);
                if (level < 0) continue;
                levels.Add(Math.Min(level, levelCap));
            }

            levels.Sort();
            levels.Reverse();
            return levels.Take(scoring.Count).Sum(level => ActiveValue + level);
        }

        private static Arrangement Evaluate(
            PlacementProblem problem, List<GridPos> cells, List<TabletPlacement> layout, SolverOptions options)
        {
            var occupancy = OptimisticOccupancy(cells, layout, problem);
            var taken = new HashSet<GridPos>(layout.Select(p => p.Position));
            var free = cells.Where(cell => !taken.Contains(cell)).ToList();

            Dictionary<int, GridPos> positions = new Dictionary<int, GridPos>();
            SimulationResult result = TabletSimulator.Run(WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);

            for (var iteration = 0; iteration < options.FixpointIterations; iteration++)
            {
                result = TabletSimulator.Run(WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);
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
                    var value = -Value(problem, problem.Charms[charmIndex], free[cellIndex], result, occupancy);
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
            PlacementProblem problem, CharmSlot charm, GridPos cell,
            SimulationResult result, GridOccupancy occupancy)
        {
            if (charm.IsFiller) return 0;
            if (Reason(charm, cell, result, problem.Grid, occupancy) != CharmInactiveReason.None) return 0;

            var level = result.EffectiveLevel(cell, charm.Enchant);
            var effective = Math.Min(charm.Definition.MaxLevel, level);

            // 상한을 넘긴 레벨은 아무 값어치가 없다. 점수가 같은 배치라면 덜 흘리는 쪽을 고르도록
            // 아주 작은 차이만 준다. 실제 점수 차이를 뒤집을 만한 크기가 아니다.
            var value = charm.Weight * (ActiveValue + effective)
                        - WastePenalty * Math.Max(0, level - effective);

            // 점수가 같은 배치가 여럿일 때 지금 자리를 지킨다. 채점할 때만 더하면 배정기가 이미
            // 자리를 바꿔 놓은 뒤라, 이득이 없는데도 맞바꾸라는 제안이 나온다.
            if (problem.CurrentCharms.TryGetValue(charm.InstanceId, out var current) && current == cell)
                value += StabilityBonus;

            return value;
        }

        /// <summary>
        /// 효과가 꺼졌다면 그 이유. 게임의 <c>Charm_Basic.RefreshCharm</c>이 보는 조건과 같고,
        /// 자리를 옮겨서는 풀 수 없는 무기 불일치를 먼저 본다.
        /// </summary>
        private static CharmInactiveReason Reason(
            CharmSlot charm, GridPos cell, SimulationResult result, GridSpec grid, GridOccupancy occupancy)
        {
            if (charm.IsDormant) return CharmInactiveReason.Weapon;
            if (result.IsDisabled(cell)) return CharmInactiveReason.Disabled;
            if (result.EffectiveLevel(cell, charm.Enchant) < 0) return CharmInactiveReason.NegativeLevel;

            if (result.IgnoreCriteria.TryGetValue(cell, out var ignore) && ignore > 0)
                return CharmInactiveReason.None;

            var kind = CharmCriteria.FromTypeName(charm.Definition.CriteriaType);
            return CharmCriteria.IsSatisfied(kind, cell, grid, occupancy)
                ? CharmInactiveReason.None
                : CharmInactiveReason.Criteria;
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
                occupancy.AddItem(position, !charm.IsFiller, !charm.IsFiller && charm.Definition.IsMagic);
            }
            return occupancy;
        }

        /// <summary>
        /// 지금과 같은 자리에 있는 석판마다 아주 작은 값을 더한다. 아티팩트 몫은 배정 단계에서
        /// 반영해야 뜻이 있어 <see cref="Value"/>가 따로 챙긴다.
        /// </summary>
        private static double Familiarity(PlacementProblem problem, List<TabletPlacement> layout)
        {
            var kept = 0;

            for (var i = 0; i < layout.Count && i < problem.Tablets.Count; i++)
            {
                if (!problem.CurrentTablets.TryGetValue(problem.Tablets[i].InstanceId, out var spot)) continue;
                if (spot.Position == layout[i].Position && spot.Rotation == layout[i].Rotation) kept++;
            }

            return StabilityBonus * kept;
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
            arrangement.Score += Familiarity(problem, layout);

            foreach (var charm in problem.Charms)
            {
                if (!positions.TryGetValue(charm.InstanceId, out var position))
                {
                    if (!charm.IsFiller) arrangement.InactiveCharms.Add(charm.InstanceId);
                    continue;
                }

                arrangement.CharmPositions[charm.InstanceId] = position;

                var level = result.EffectiveLevel(position, charm.Enchant);
                arrangement.Levels[position] = level;
                if (charm.IsFiller) continue;

                var reason = Reason(charm, position, result, problem.Grid, occupancy);
                if (reason != CharmInactiveReason.None)
                {
                    // 꺼진 아티팩트는 레벨이 얼마든 효과가 없다. 칸에 레벨을 그대로 보여 주면
                    // 켜져 있는 것처럼 읽힌다.
                    arrangement.EffectiveLevels[position] = 0;
                    arrangement.InactiveCells[position] = reason;
                    arrangement.InactiveCharms.Add(charm.InstanceId);
                    continue;
                }

                arrangement.EffectiveLevels[position] = Math.Max(0, Math.Min(charm.Definition.MaxLevel, level));
                arrangement.Score += Value(problem, charm, position, result, occupancy);
            }
            return arrangement;
        }
    }
}
