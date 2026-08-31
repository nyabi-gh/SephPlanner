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
        /// 빔을 좁힐 때 쓰는, 켜져 있는 아티팩트 하나의 값어치. 레벨은 세기를 더할 뿐이고 레벨 0
        /// 도 살아 있다. 이것이 없으면 꺼지는 자리(레벨 음수)와 레벨 0 자리가 똑같이 0점이라,
        /// 아티팩트를 꺼진 채로 두고도 최적이라고 하게 된다.
        ///
        /// 실제 채점은 아티팩트마다 다른 <see cref="CharmWorth"/>를 쓴다. 여기서까지 그러지 않는
        /// 것은 이 어림값이 "어느 아티팩트가 어디 갈지" 정하기 전에 배치만 줄 세우는 값이기
        /// 때문이다. 어느 배치든 같은 잣대로 재기만 하면 되고, 뽑힌 배치는 뒤에서 다시 채점된다.
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

            var searched = SearchTabletLayouts(problem, cells, options);

            // 놓을 자리가 모자라면 탐색이 석판 일부를 뺀 배치를 내놓는다. 그런 배치를 그대로
            // 채점하면 존재하는 석판을 무시한 점수를 최적이라고 말하게 되므로, 완전한 배치가
            // 하나라도 있으면 불완전한 것은 버린다.
            var candidates = searched
                .Where(layout => layout.Count == problem.Tablets.Count)
                .Take(options.ExactCandidates)
                .ToList();

            // 탐색이 현재 배치를 후보에서 떨어뜨리면, 이미 최적인 배치를 두고도 옮기라고 하게 된다.
            var asIs = CurrentLayout(problem);
            if (asIs != null) candidates.Insert(0, asIs);

            // 완전한 배치가 아예 없으면(석판이 열린 칸보다 많은 극단) 놓을 수 있는 만큼이라도
            // 평가하되, Describe 가 빠진 수를 UnplacedTablets 로 남겨 호출자가 알 수 있게 한다.
            if (candidates.Count == 0)
                candidates = searched.Take(options.ExactCandidates).ToList();

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
                // 돌릴 수 없는 석판은 지금 돌아가 있는 각도 그대로만 쓴다. 0으로 고정하면
                // 이미 돌아간 채로 잠긴 석판(저주 등)에 불가능한 회전을 제안하게 된다.
                var rotatable = slot.Rotatable;
                var fixedRotation = problem.CurrentTablets.TryGetValue(slot.InstanceId, out var spot)
                    ? spot.Rotation
                    : 0;

                var expanded = new List<(List<TabletPlacement> Layout, double Score)>();

                foreach (var layout in beam)
                {
                    var taken = new HashSet<GridPos>(layout.Select(p => p.Position));

                    foreach (var cell in cells)
                    {
                        if (taken.Contains(cell)) continue;

                        for (var rotation = 0; rotation < (rotatable ? 4 : 1); rotation++)
                        {
                            var next = new List<TabletPlacement>(layout)
                            {
                                slot.At(cell, rotatable ? rotation : fixedRotation),
                            };
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
            Dictionary<GridPos, CharmSlot>? neighbors = null;
            SimulationResult result = TabletSimulator.Run(WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);

            for (var iteration = 0; iteration < options.FixpointIterations; iteration++)
            {
                var next = Assign(problem, free, result, occupancy, neighbors);
                if (SamePositions(positions, next)) break;

                positions = next;
                occupancy = OccupancyFrom(layout, positions, problem);
                neighbors = CharmsByCell(problem, positions);

                // 배정이 바뀔 때마다 그 배치 기준으로 다시 시뮬레이션한다. 반복이 소진돼 수렴하지
                // 못하고 빠져나가도, result 는 언제나 마지막 positions 와 같은 상태를 보고 있어야
                // 보고되는 점수가 실제 배치의 점수가 된다.
                result = TabletSimulator.Run(WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);
            }

            return Describe(problem, layout, positions, occupancy, result);
        }

        private static Dictionary<GridPos, CharmSlot> CharmsByCell(
            PlacementProblem problem, Dictionary<int, GridPos> positions)
        {
            var map = new Dictionary<GridPos, CharmSlot>();
            foreach (var charm in problem.Charms)
            {
                if (positions.TryGetValue(charm.InstanceId, out var position)) map[position] = charm;
            }
            return map;
        }

        private static Dictionary<int, GridPos> Assign(
            PlacementProblem problem, List<GridPos> free, SimulationResult result, GridOccupancy occupancy,
            Dictionary<GridPos, CharmSlot>? neighbors)
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
                    var value = -Value(problem, problem.Charms[charmIndex], free[cellIndex], result, occupancy, neighbors);
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
            SimulationResult result, GridOccupancy occupancy, Dictionary<GridPos, CharmSlot>? neighbors)
        {
            // 필러도 자리 유지 몫은 받아야 한다. 없으면 전 칸이 0점 동률이라 배정 순서에 따라
            // 필러끼리 자리를 맞바꾸는 제안이 나온다.
            if (charm.IsFiller)
            {
                return problem.CurrentCharms.TryGetValue(charm.InstanceId, out var kept) && kept == cell
                    ? StabilityBonus
                    : 0;
            }
            if (Reason(charm, cell, result, problem.Grid, occupancy) != CharmInactiveReason.None) return 0;

            var level = result.EffectiveLevel(cell, charm.Enchant);
            var effective = Math.Min(charm.Definition.MaxLevel, level);

            // 상한을 넘긴 레벨은 아무 값어치가 없다. 점수가 같은 배치라면 덜 흘리는 쪽을 고르도록
            // 아주 작은 차이만 준다. 실제 점수 차이를 뒤집을 만한 크기가 아니다.
            var value = charm.Weight * charm.Worth.At(effective)
                        - WastePenalty * Math.Max(0, level - effective);

            if (charm.Definition.Behavior == "Charm_WhitePaper")
                value += WhitePaperWorth(problem, charm, cell, neighbors);

            if (charm.Definition.Behavior == "Charm_NearLevelDamage")
                value += NearLevelDamageWorth(charm, cell, effective, result, neighbors);

            // 점수가 같은 배치가 여럿일 때 지금 자리를 지킨다. 채점할 때만 더하면 배정기가 이미
            // 자리를 바꿔 놓은 뒤라, 이득이 없는데도 맞바꾸라는 제안이 나온다.
            if (problem.CurrentCharms.TryGetValue(charm.InstanceId, out var current) && current == cell)
                value += StabilityBonus;

            return value;
        }

        /// <summary>
        /// 하얀 종이는 양옆 아티팩트가 공유하는 카테고리를 물려받아 콤보 개수에 +1 을 보탠다
        /// (게임 <c>Charm_WhitePaper</c>: 좌우 이웃의 카테고리 중 둘 다 가진 것을 자기 것으로).
        /// 이웃은 직전 반복의 배정에서 오는 근사이고, 개수도 지금 배치 기준이라 정확히는 못 세지만
        /// 같은 카테고리 쌍 사이에 끼우는 방향으로는 충분히 이끈다.
        /// </summary>
        private static double WhitePaperWorth(
            PlacementProblem problem, CharmSlot charm, GridPos cell, Dictionary<GridPos, CharmSlot>? neighbors)
        {
            if (neighbors is null || problem.Combos is null) return 0;
            if (!neighbors.TryGetValue(cell.Offset(-1, 0), out var left) || left == charm || left.IsFiller) return 0;
            if (!neighbors.TryGetValue(cell.Offset(1, 0), out var right) || right == charm || right.IsFiller) return 0;

            var worth = 0.0;
            foreach (var category in left.Definition.Categories)
            {
                if (!right.Definition.Categories.Contains(category)) continue;

                var combo = problem.Combos(category);
                if (combo is null) continue;

                var count = 0;
                problem.ComboCounts?.TryGetValue(category, out count);
                worth += Worth.OfComboStep(combo, count, out _, out _);
            }
            return worth;
        }

        /// <summary>이웃 여덟 칸. 게임 <c>Charm_NearLevelDamage.directions</c>와 같은 집합이다.</summary>
        private static readonly (int X, int Y)[] Around =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1),
        };

        /// <summary>
        /// 조화의 수정은 이웃 여덟 칸에 있는 아티팩트들의 유효 레벨을 모두 더한 만큼 전체 피해를
        /// 올린다(게임 <c>Charm_NearLevelDamage</c>: 칸마다 <c>min(레벨, 상한)</c>을 더하고 자기
        /// 레벨에 해당하는 배수를 곱한다). 자기 레벨만 보는 점수로는 이 아티팩트를 구석에 두든
        /// 한가운데 두든 똑같아 보이므로, 자리 가치를 여기서 되살린다.
        ///
        /// 하얀 종이와 같은 근사가 걸린다 — 이웃은 직전 반복의 배정 결과라, 이웃을 이 아티팩트
        /// 주위로 다시 모으는 탐색까지는 하지 못하고 이미 모여 있는 자리를 찾아간다.
        /// </summary>
        private static double NearLevelDamageWorth(
            CharmSlot charm, GridPos cell, int ownLevel,
            SimulationResult result, Dictionary<GridPos, CharmSlot>? neighbors)
        {
            var table = charm.Definition.NeighborLevelBonus;
            if (neighbors is null || table.Count == 0) return 0;

            var perLevel = table[Math.Min(Math.Max(ownLevel, 0), table.Count - 1)];
            if (perLevel == 0) return 0;

            var sum = 0;
            foreach (var (dx, dy) in Around)
            {
                var spot = cell.Offset(dx, dy);
                if (!neighbors.TryGetValue(spot, out var neighbor)) continue;

                // 직전 반복에서 자기가 서 있던 칸이 이웃으로 잡히는 경우다. 그대로 세면 자기를
                // 세는 셈이고 건너뛰면 빈 칸으로 치는데, 배정은 자리 맞바꾸기라 실제로는 지금
                // 목표 칸에 있는 아티팩트가 그 자리를 채우게 된다. 그것으로 갈음한다.
                if (neighbor == charm && !neighbors.TryGetValue(cell, out neighbor)) continue;
                if (neighbor == charm || neighbor.IsFiller) continue;

                // 게임은 상한만 씌우고 아래로는 자르지 않는다. 음수 레벨 이웃은 오히려 깎는다.
                sum += Math.Min(
                    result.EffectiveLevel(spot, neighbor.Enchant), neighbor.Definition.MaxLevel);
            }
            return perLevel * sum * Worth.DamageBonus;
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

            return CharmCriteria.IsSatisfied(charm.Criteria, cell, grid, occupancy)
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
            arrangement.UnplacedTablets = problem.Tablets.Count - layout.Count;
            arrangement.Score += Familiarity(problem, layout);

            // 하얀 종이 같은 이웃 의존 가치를 최종 배치 기준으로 다시 매긴다.
            var neighbors = CharmsByCell(problem, positions);

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
                arrangement.Score += Value(problem, charm, position, result, occupancy, neighbors);
            }

            // 아티팩트가 놓인 칸은 인챈트가 더해진 위 값을, 나머지 칸은 시뮬레이션 값을 쓴다.
            for (var index = 0; index < problem.Grid.Storage; index++)
            {
                var cell = problem.Grid.ToPosition(index);
                arrangement.CellLevels[cell] = arrangement.Levels.TryGetValue(cell, out var withEnchant)
                    ? withEnchant
                    : result.EffectiveLevel(cell, 0);
            }
            return arrangement;
        }
    }
}
