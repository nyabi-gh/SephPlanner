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
    ///
    /// <b>두 단계가 같은 것을 재야 한다.</b> 1단계가 어림값으로 후보를 버리고 2단계만 진짜
    /// 점수를 보면, 1단계에서 잘못 버린 것은 되찾을 길이 없다. 이 클래스가 겪은 문제가 그것이고
    /// 세 자리에서 고쳤다.
    ///
    /// <list type="number">
    /// <item>어림값(<see cref="EstimateModel"/>)이 아티팩트마다 다른 값어치와 사용자가 찍은
    /// 강화 우선까지 본다. 전에는 "켜져 있으면 1, 레벨 하나에 1"이라 어느 아티팩트가 센지 몰랐다.</item>
    /// <item>빔을 자를 때 한 부모가 다 가져가지 못하게 한다(Select). 좋은 부분 배치 하나에서
    /// 나온 사촌들이 빔을 메우면 폭을 넓혀도 보는 넓이가 안 늘어난다.</item>
    /// <item>이긴 배치를 자리 맞바꾸기로 다듬는다(Polish). 배정 비용이 직전 반복의 이웃을 보는
    /// 근사라, 두 아티팩트가 함께 움직여야 좋아지는 수를 배정기 혼자서는 못 넘는다.</item>
    /// </list>
    ///
    /// 셋을 넣기 전에는 <b>빔을 여덟 배로 넓혀도 점수가 안 올랐다</b>. 잘못된 잣대로 400개를
    /// 남기든 3200개를 남기든 같은 것만 남기 때문이다. 넣은 뒤에는 빔 400 이 예전의 빔 1600 보다
    /// 좋으면서 네 배 빠르다. 폭을 늘리는 것이 답이 아니었다는 뜻이므로, "정밀 탐색" 같은
    /// 시간을 더 쓰는 모드는 두지 않는다.
    /// </summary>
    public static class PlacementSolver
    {
        private const double WastePenalty = 1e-4;

        /// <summary>낭비 판단보다도 작게 두어, 정말 우열이 없을 때만 현 상태를 유지하도록 한다.</summary>
        private const double StabilityBonus = 1e-6;

        /// <summary>
        /// 직전 제안과 같은 자리에 주는 몫. 지금 자리보다 약해야 한다 - 옮길 필요가 없어진 것은
        /// 그대로 두는 쪽이 먼저고, 어차피 옮길 것이라면 저번에 말한 자리가 먼저다.
        /// 격자 42칸이 다 맞아도 합이 StabilityBonus 하나를 넘지 않도록 잡았다(42 x 2e-8 &lt; 1e-6).
        /// </summary>
        private const double PlanBonus = 2e-8;

        public static Arrangement Solve(PlacementProblem problem, SolverOptions? options = null)
        {
            options ??= new SolverOptions();
            return EvaluateLayouts(problem, SearchLayouts(problem, options), options);
        }

        /// <summary>
        /// 채점까지 가 볼 석판 배치 후보들.
        ///
        /// <b>풀이 비용의 대부분이 여기다</b> - 42칸 판에서 재어 보면 <see cref="Solve"/>의 97%가
        /// 이 탐색이고, 나머지 3%가 <see cref="EvaluateLayouts"/>의 정확한 배정이다. 그래서 같은
        /// 석판 구성을 여러 번 채점해야 할 때는 이것을 한 번만 짓고 돌려 쓴다
        /// (<see cref="LayoutCache"/>).
        /// </summary>
        public static List<List<TabletPlacement>> SearchLayouts(
            PlacementProblem problem, SolverOptions? options = null)
        {
            options ??= new SolverOptions();

            var cells = Cells(problem);
            var model = BuildEstimateModel(problem);
            var searched = SearchTabletLayouts(problem, cells, options, model);

            // 놓을 자리가 모자라면 탐색이 석판 일부를 뺀 배치를 내놓는다. 그런 배치를 그대로
            // 채점하면 존재하는 석판을 무시한 점수를 최적이라고 말하게 되므로, 완전한 배치가
            // 하나라도 있으면 불완전한 것은 버린다.
            var candidates = searched
                .Where(layout => layout.Count == problem.Tablets.Count)
                .Take(options.ExactCandidates)
                .ToList();

            // 탐색이 현재 배치를 후보에서 떨어뜨리면, 이미 최적인 배치를 두고도 옮기라고 하게 된다.
            var asIs = Layout(problem, problem.CurrentTablets);
            if (asIs != null) candidates.Insert(0, asIs);

            // 직전 제안의 배치도 마찬가지다. 빔이 떨어뜨리면 같은 점수의 다른 배치로 갈아타
            // 따라가던 계획이 통째로 다시 쓰인다.
            var asPlanned = PlannedLayout(problem, cells, model);
            if (asPlanned != null) candidates.Insert(0, asPlanned);

            // 완전한 배치가 아예 없으면(석판이 열린 칸보다 많은 극단) 놓을 수 있는 만큼이라도
            // 평가하되, Describe 가 빠진 수를 UnplacedTablets 로 남겨 호출자가 알 수 있게 한다.
            if (candidates.Count == 0)
                candidates = searched.Take(options.ExactCandidates).ToList();

            return candidates;
        }

        /// <summary>
        /// 주어진 배치 후보들을 정확히 채점해 가장 좋은 것을 고른다. 석판이 고정되면 아티팩트
        /// 배치는 배정 문제라 여기서 헝가리안으로 정확히 풀린다.
        ///
        /// 다만 배정 비용이 이웃에 기대는 아티팩트(하얀 종이, 조화의 수정, 북향의 침, 거대한
        /// 망원경, 헌신의 휘장)에서는 그 "정확히"가 깨진다 - 비용이 직전 반복의 배정을 보고
        /// 매겨지기 때문이다. 그래서 이긴 배치 하나만 마지막에 <see cref="Polish"/>로 다듬는다.
        /// </summary>
        public static Arrangement EvaluateLayouts(
            PlacementProblem problem, IReadOnlyList<List<TabletPlacement>> layouts,
            SolverOptions? options = null)
        {
            options ??= new SolverOptions();

            var cells = Cells(problem);
            Arrangement? best = null;
            List<TabletPlacement>? bestLayout = null;
            foreach (var layout in layouts)
            {
                if (best != null && options.Cancellation.IsCancellationRequested) break;

                var arrangement = Evaluate(problem, cells, layout, options);
                if (best != null && arrangement.Score <= best.Score) continue;

                best = arrangement;
                bestLayout = layout;
            }
            if (best is null) return Evaluate(problem, cells, new List<TabletPlacement>(), options, polish: true);

            // 다듬기는 이긴 배치에만 건다. 후보마다 걸면 O(아티팩트^3)가 후보 수만큼 곱해져
            // 실시간 폴링이 못 따라온다 - 재어 보면 풀이 시간이 두 자릿수 배로 뛴다.
            if (options.PolishPasses <= 0 || options.Cancellation.IsCancellationRequested) return best;

            var polished = Evaluate(problem, cells, bestLayout!, options, polish: true);
            return polished.Score > best.Score ? polished : best;
        }

        private static List<GridPos> Cells(PlacementProblem problem) =>
            Enumerable.Range(0, problem.Grid.Storage)
                      .Select(problem.Grid.ToPosition)
                      .ToList();

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

        private static List<TabletPlacement>? Layout(
            PlacementProblem problem, Dictionary<int, TabletSpot> spots)
        {
            if (problem.Tablets.Count == 0 || spots.Count < problem.Tablets.Count) return null;

            var layout = new List<TabletPlacement>(problem.Tablets.Count);
            var taken = new HashSet<GridPos>();

            foreach (var slot in problem.Tablets)
            {
                if (!spots.TryGetValue(slot.InstanceId, out var spot)) return null;
                if (!taken.Add(spot.Position)) return null;
                layout.Add(slot.At(spot.Position, spot.Rotation));
            }
            return layout;
        }

        /// <summary>
        /// Estimate 의 낙관적 점유. "빈 칸에는 다 아이템이 있다"는 가정을 집합에 칸마다 넣는 대신
        /// 좌표 계산으로 답한다 - 최심부 루프에서 호출마다 해시셋 백여 건을 채우고 있었다.
        /// </summary>
        private sealed class EstimateOccupancy : GridOccupancy
        {
            private readonly GridSpec _grid;
            private readonly bool _anyMagic;
            private readonly bool[] _taken;

            public EstimateOccupancy(GridSpec grid, bool anyMagic)
            {
                _grid = grid;
                _anyMagic = anyMagic;
                _taken = new bool[grid.Width * grid.Height];
            }

            public void Take(GridPos position)
            {
                if (Open(position)) _taken[_grid.ToIndex(position.X, position.Y)] = true;
            }

            private bool Open(GridPos position) =>
                position.X >= 0 && position.X < _grid.Width &&
                position.Y >= 0 && position.Y < _grid.Height &&
                _grid.ToIndex(position.X, position.Y) < _grid.Storage;

            // OptimisticOccupancy 와 같은 답이다: 열린 칸에는 전부 아이템이 있고,
            // 석판이 차지한 칸만 아티팩트가 아니다.
            public override bool HasItem(GridPos position) => Open(position);
            public override bool HasCharm(GridPos position) =>
                Open(position) && !_taken[_grid.ToIndex(position.X, position.Y)];
            public override bool HasMagicCharm(GridPos position) => _anyMagic && HasCharm(position);
        }

        /// <summary>
        /// 빔이 배치를 줄 세울 때 쓰는 잣대.
        ///
        /// <b>여기가 어림값의 전부다.</b> 예전에는 "켜져 있는 아티팩트 하나에 1, 레벨 하나에 1"
        /// 이라는 한 가지 잣대로 모두를 쟀다. 그래서 빔은 <b>어느 아티팩트가 센지도, 사용자가
        /// 무엇을 강화 우선으로 찍었는지도 모른 채</b> 배치를 골랐다 - 레벨 5 칸 하나를 만드는
        /// 배치와 레벨 5 칸 하나를 만드는 다른 배치가, 그 칸에 갈 아티팩트가 전설이든 잡템이든
        /// 똑같은 값으로 보였다. 빔을 여덟 배로 넓혀도 점수가 안 오르던 까닭이 이것이다.
        /// 잘못된 잣대로 400개를 남기든 3200개를 남기든 같은 것만 남는다.
        ///
        /// 이제 아티팩트를 값어치 순으로 세워 두고, 레벨이 높은 칸부터 값어치가 큰 아티팩트를
        /// 짝지어 본다. 재배열 부등식이라 값이 레벨에 대해 늘기만 하면 이 짝짓기가 가장 큰 합을
        /// 주고, 그래서 어림값은 실제 배정이 낼 수 있는 값의 위쪽 어림이 된다.
        /// </summary>
        private sealed class EstimateModel
        {
            public int LevelCap;
            public bool AnyMagic;

            /// <summary>값어치 순위별, 레벨별 값어치. <c>[순위][레벨]</c>. 풀이마다 한 번만 짓는다.</summary>
            public double[][] ValueByRank = Array.Empty<double[]>();
        }

        /// <summary>
        /// 한 아티팩트가 그 레벨의 칸에서 갖는 값어치. <see cref="Value"/>가 매기는 것과 같은
        /// 잣대이되, 자리에 달린 몫(조건·이웃·안정)은 뺀 것이다 - 아직 어느 칸인지 모르기 때문이다.
        /// </summary>
        private static double RankValue(CharmSlot charm, int level) =>
            charm.Weight * charm.Worth.At(Math.Min(charm.Definition.MaxLevel, level));

        private static EstimateModel BuildEstimateModel(PlacementProblem problem)
        {
            var scoring = new List<CharmSlot>(problem.Charms.Count);
            var levelCap = 0;
            var anyMagic = false;

            foreach (var charm in problem.Charms)
            {
                if (charm.Definition.IsMagic) anyMagic = true;
                if (charm.IsFiller || charm.IsDormant) continue;

                scoring.Add(charm);
                levelCap = Math.Max(levelCap, charm.Definition.MaxLevel);
            }
            if (scoring.Count == 0) levelCap = 5;

            // 상한에서의 값어치로 줄 세운다. 레벨마다 순서가 뒤바뀔 수는 있지만(상한이 낮은
            // 아티팩트는 낮은 레벨에서만 앞선다) 빔을 좁히는 잣대에는 한 줄이면 넉넉하다.
            scoring.Sort((a, b) => RankValue(b, levelCap).CompareTo(RankValue(a, levelCap)));

            var table = new double[scoring.Count][];
            for (var rank = 0; rank < scoring.Count; rank++)
            {
                var row = new double[levelCap + 1];
                for (var level = 0; level <= levelCap; level++) row[level] = RankValue(scoring[rank], level);
                table[rank] = row;
            }

            return new EstimateModel { LevelCap = levelCap, AnyMagic = anyMagic, ValueByRank = table };
        }

        /// <summary>
        /// 직전 제안의 배치를 후보로 되살린다. 계획에 없는 새 석판이 끼면 계획된 자리는 그대로
        /// 두고 새 것만 남는 칸에서 탐욕으로 앉힌다 - 새 석판이 올 때마다 앵커가 통째로 사라지면,
        /// 정확히 개편이 가장 큰 그 순간에 계획이 다시 쓰인다(실측 녹화에서 그랬다).
        /// </summary>
        private static List<TabletPlacement>? PlannedLayout(
            PlacementProblem problem, List<GridPos> cells, EstimateModel model)
        {
            if (problem.Tablets.Count == 0 || problem.PlannedTablets.Count == 0) return null;

            // 직전 계획은 그때의 상황에서 나온 것이라 지금은 실행할 수 없을 수 있다. 그 사이에
            // 회전이 잠겼거나(저주) 가방이 줄어 칸이 닫혔으면, 그 석판만 계획에서 떼어 아래 탐욕
            // 배치로 넘긴다. 그대로 두면 계획이 스스로를 되먹여 불가능한 지시가 영영 남는다 -
            // HUD 는 따라 할 수 없는 걸음을 보여주고 자동 배치는 매번 회전 잠금에서 물러선다.
            var open = new HashSet<GridPos>(cells);
            var planned = new Dictionary<int, TabletSpot>();
            foreach (var slot in problem.Tablets)
            {
                if (!problem.PlannedTablets.TryGetValue(slot.InstanceId, out var spot)) continue;
                if (!open.Contains(spot.Position)) continue;
                if (!slot.Rotatable && spot.Rotation != CurrentRotation(problem, slot)) continue;
                planned[slot.InstanceId] = spot;
            }
            if (planned.Count == 0) return null;

            var reserved = new HashSet<GridPos>();
            foreach (var spot in planned.Values)
            {
                if (!reserved.Add(spot.Position)) return null;
            }

            // 후보 배치는 problem.Tablets 순서를 지켜야 한다. Describe 와 Targets 가 같은
            // 순번끼리 짝짓는다.
            var layout = new List<TabletPlacement>(problem.Tablets.Count);
            foreach (var slot in problem.Tablets)
            {
                if (planned.TryGetValue(slot.InstanceId, out var spot))
                {
                    layout.Add(slot.At(spot.Position, spot.Rotation));
                    continue;
                }

                var rotations = DistinctRotations(slot, CurrentRotation(problem, slot));

                List<TabletPlacement>? grown = null;
                var bestScore = double.NegativeInfinity;
                var bestCell = default(GridPos);
                foreach (var cell in cells)
                {
                    if (reserved.Contains(cell)) continue;
                    foreach (var rotation in rotations)
                    {
                        var trial = new List<TabletPlacement>(layout) { slot.At(cell, rotation) };
                        var estimate = Estimate(problem, cells, trial, model);
                        if (estimate > bestScore)
                        {
                            bestScore = estimate;
                            grown = trial;
                            bestCell = cell;
                        }
                    }
                }
                if (grown == null) return null;

                layout = grown;
                reserved.Add(bestCell);
            }
            return layout;
        }

        private static List<List<TabletPlacement>> SearchTabletLayouts(
            PlacementProblem problem, List<GridPos> cells, SolverOptions options, EstimateModel model)
        {
            var beam = new List<List<TabletPlacement>> { new List<TabletPlacement>() };
            var twin = Twins(problem);

            for (var index = 0; index < problem.Tablets.Count; index++)
            {
                var slot = problem.Tablets[index];

                // 이 풀이를 버릴 것이 이미 정해졌으면 여기서 그만둔다. 석판 한 장을 놓는 단계마다
                // 보는 것으로 충분하다 - 비용이 거기에 몰려 있다.
                if (options.Cancellation.IsCancellationRequested) break;

                // 돌릴 수 없는 석판은 지금 돌아가 있는 각도 그대로만 쓴다. 0으로 고정하면
                // 이미 돌아간 채로 잠긴 석판(저주 등)에 불가능한 회전을 제안하게 된다.
                var rotations = DistinctRotations(slot, CurrentRotation(problem, slot));

                var expanded = new List<(List<TabletPlacement> Layout, double Score, int Parent)>();

                for (var parent = 0; parent < beam.Count; parent++)
                {
                    var layout = beam[parent];
                    var taken = new HashSet<GridPos>(layout.Select(p => p.Position));

                    // 똑같은 석판끼리는 자리를 맞바꿔도 같은 배치다. 앞선 쌍둥이보다 뒤쪽 칸만
                    // 보게 해 그 순열들을 한 번씩만 만든다.
                    var floor = twin[index] >= 0 && twin[index] < layout.Count
                        ? problem.Grid.ToIndex(layout[twin[index]].Position.X, layout[twin[index]].Position.Y)
                        : -1;

                    foreach (var cell in cells)
                    {
                        if (taken.Contains(cell)) continue;
                        if (floor >= 0 && problem.Grid.ToIndex(cell.X, cell.Y) <= floor) continue;

                        foreach (var rotation in rotations)
                        {
                            var next = new List<TabletPlacement>(layout)
                            {
                                slot.At(cell, rotation),
                            };
                            expanded.Add((next, Estimate(problem, cells, next, model), parent));
                        }
                    }
                }

                if (expanded.Count == 0) break;

                beam = Select(expanded, beam.Count, options);
            }
            return beam;
        }

        /// <summary>
        /// 다음 빔에 남길 것을 고른다. 점수 순으로 자르되 <b>한 부모가 빔을 통째로 차지하지
        /// 못하게</b> 한다.
        ///
        /// 그냥 상위 N 을 자르면 좋은 부분 배치 하나에서 나온 사촌들 - 석판 한 장만 옆 칸으로
        /// 옮긴 것들 - 이 빔을 메운다. 서로 거의 같은 것을 400개 들고 다음 단계로 가는 셈이라,
        /// 폭을 넓혀도 보는 넓이가 안 늘었다(빔 800 위로는 점수가 오르지 않고 1200 에서는
        /// 오히려 떨어지는 것이 그 자국이다). 부모마다 몫을 정해 두면 같은 폭으로 훨씬 많은
        /// 갈래를 들고 간다.
        ///
        /// 몫을 다 쓰고도 자리가 남으면 남은 것 중 점수 순으로 채운다 - 부모가 적을 때(첫 단계는
        /// 하나뿐이다) 빔을 비워 두지 않기 위해서다.
        /// </summary>
        private static List<List<TabletPlacement>> Select(
            List<(List<TabletPlacement> Layout, double Score, int Parent)> expanded,
            int parents, SolverOptions options)
        {
            expanded.Sort((a, b) => b.Score.CompareTo(a.Score));

            var quota = options.BeamWidth;
            if (parents > 1 && options.ParentQuota > 0)
                quota = Math.Max(options.ParentQuota, options.BeamWidth / parents);

            var chosen = new List<List<TabletPlacement>>(Math.Min(options.BeamWidth, expanded.Count));
            var used = new Dictionary<int, int>(parents);
            var skipped = new List<List<TabletPlacement>>();

            foreach (var entry in expanded)
            {
                if (chosen.Count >= options.BeamWidth) break;

                used.TryGetValue(entry.Parent, out var count);
                if (count >= quota)
                {
                    if (skipped.Count < options.BeamWidth) skipped.Add(entry.Layout);
                    continue;
                }

                used[entry.Parent] = count + 1;
                chosen.Add(entry.Layout);
            }

            for (var i = 0; i < skipped.Count && chosen.Count < options.BeamWidth; i++)
                chosen.Add(skipped[i]);

            return chosen;
        }

        /// <summary>
        /// 슬롯마다, 저와 완전히 같은 앞선 슬롯의 번호(없으면 -1).
        ///
        /// 같은 석판을 여럿 들고 있으면 빔이 순열 중복으로 낭비된다 - 자리만 맞바꾼 배치는 효과가
        /// 같아 추정치도 같으므로, 세 장이면 여섯 벌이 빔 400 자리를 나눠 먹는다. 실효 폭이 1/6 로
        /// 줄어드는 셈이다. 앞선 쌍둥이보다 뒤쪽 칸만 쓰게 하면 그중 한 벌만 남는다.
        ///
        /// 현재 배치와 직전 제안은 이 규칙을 어겨도 <see cref="SearchLayouts"/>가 후보에 직접
        /// 넣으므로 잃지 않는다.
        /// </summary>
        private static int[] Twins(PlacementProblem problem)
        {
            var twin = new int[problem.Tablets.Count];
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var index = 0; index < problem.Tablets.Count; index++)
            {
                var slot = problem.Tablets[index];

                // 회전이 잠긴 석판은 지금 각도가 곧 제 모양이라 쌍둥이 판정에 들어간다.
                var signature = string.Join("", new[]
                {
                    slot.Definition.EntityId.ToString(),
                    slot.InstanceQuery ?? slot.Definition.Query,
                    slot.InstanceConditionQuery ?? slot.Definition.ConditionQuery,
                    slot.Rotatable ? "r" : "f" + CurrentRotation(problem, slot).ToString(),
                });

                twin[index] = seen.TryGetValue(signature, out var previous) ? previous : -1;
                seen[signature] = index;
            }
            return twin;
        }

        /// <summary>
        /// 실제 효과가 다른 회전만, 지금 각도부터 세어 남긴다. 대칭 질의 석판(쌍성의 위아래 등)의
        /// 회전은 효과가 같아, 걸러내지 않으면 "회전 3 → 1" 같은 아무 일도 하지 않는 회전 지시가
        /// 나온다 - 실제 세션에서 관측됐다.
        /// </summary>
        /// <summary>지금 돌아가 있는 각도. 아직 집지 않은 석판은 0 이다.</summary>
        private static int CurrentRotation(PlacementProblem problem, TabletSlot slot) =>
            problem.CurrentTablets.TryGetValue(slot.InstanceId, out var spot) ? spot.Rotation : 0;

        private static List<int> DistinctRotations(TabletSlot slot, int currentRotation)
        {
            if (!slot.Rotatable) return new List<int> { currentRotation };

            var rotations = new List<int>(4);
            var seen = new HashSet<string>();
            for (var step = 0; step < 4; step++)
            {
                var rotation = (currentRotation + step) % 4;
                if (seen.Add(RotationSignature(slot, rotation))) rotations.Add(rotation);
            }
            return rotations;
        }

        private static string RotationSignature(TabletSlot slot, int rotation)
        {
            var query = slot.InstanceQuery ?? slot.Definition.Query;
            var condition = slot.InstanceConditionQuery ?? slot.Definition.ConditionQuery;
            return Canonical(TabletQuery.Rotated(query, rotation)) + "|" +
                   Canonical(TabletQuery.Rotated(condition, rotation));
        }

        /// <summary>줄 순서만 다른 질의는 같은 효과다(누적이 전부 교환법칙을 탄다).</summary>
        private static string Canonical(string query)
        {
            if (string.IsNullOrEmpty(query)) return "";

            var lines = query
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(line => line, StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        /// <summary>
        /// 아티팩트를 실제로 배정하지 않고 매기는 값. 빔을 좁히는 용도이므로 정확할 필요는 없고
        /// 유망한 배치를 위로 올리기만 하면 된다. 탐색의 최심부라 정렬·집합 할당을 두지 않는다 -
        /// 레벨 분포를 세어 위에서부터, 값어치 순으로 세워 둔 아티팩트와 짝지어 거둔다
        /// (<see cref="EstimateModel"/>).
        ///
        /// 칸 하나가 더 켜지면 그 다음 순위의 값어치가 더해지고 그 값은 음수가 아니므로, 아티팩트를
        /// 꺼진 채로 두는 배치가 이길 수 없다 - 예전에 상수 하나로 지키던 성질이 짝짓기 구조
        /// 자체에서 나온다.
        /// </summary>
        private static double Estimate(
            PlacementProblem problem, List<GridPos> cells, List<TabletPlacement> layout, EstimateModel model)
        {
            var occupancy = new EstimateOccupancy(problem.Grid, model.AnyMagic);
            foreach (var placement in layout) occupancy.Take(placement.Position);
            var result = TabletSimulator.Run(WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);

            var counts = new int[model.LevelCap + 1];
            foreach (var cell in cells)
            {
                if (!occupancy.HasCharm(cell)) continue;
                if (result.IsDisabled(cell)) continue;

                var level = result.EffectiveLevel(cell, 0);
                if (level < 0) continue;
                counts[Math.Min(level, model.LevelCap)]++;
            }

            var total = 0.0;
            var rank = 0;
            var ranks = model.ValueByRank.Length;
            for (var level = model.LevelCap; level >= 0 && rank < ranks; level--)
            {
                var take = Math.Min(counts[level], ranks - rank);
                for (var taken = 0; taken < take; taken++) total += model.ValueByRank[rank++][level];
            }

            // 동점인 배치가 많다. 무엇을 남길지 정렬이 우연히 정하게 두면, 아무것도 달라지지
            // 않았는데 폴링마다 다른 배치가 살아남는다. 채점 때와 같은 잣대로 지금 자리·직전
            // 제안을 지키는 쪽을 위에 올린다 - 실제 점수 차이를 뒤집을 수 없는 크기다.
            return total + Familiarity(problem, layout);
        }

        private static Arrangement Evaluate(
            PlacementProblem problem, List<GridPos> cells, List<TabletPlacement> layout, SolverOptions options,
            bool polish = false)
        {
            var occupancy = OptimisticOccupancy(cells, layout, problem);
            var taken = new HashSet<GridPos>(layout.Select(p => p.Position));
            var free = cells.Where(cell => !taken.Contains(cell)).ToList();

            Dictionary<int, GridPos> positions = new Dictionary<int, GridPos>();
            Dictionary<GridPos, CharmSlot>? neighbors = null;
            SimulationResult result = TabletSimulator.Run(WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);

            var bestPositions = positions;
            var bestOccupancy = occupancy;
            var bestResult = result;
            var bestScore = double.NegativeInfinity;

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

                // 조건부 아티팩트는 배정과 조건이 서로 물려 2주기로 진동할 수 있고, 반복이 나쁜
                // 쪽 위상에서 끝날 수 있다. 마지막을 그대로 돌려주면 같은 판의 점수가 폴링마다
                // 달라져 이긴 석판 배치가 뒤바뀐다. 그래서 지나온 것 중 최선을 들고 있는다.
                var score = ScoreOf(problem, layout, positions, occupancy, result, neighbors);
                if (score <= bestScore) continue;

                bestScore = score;
                bestPositions = positions;
                bestOccupancy = occupancy;
                bestResult = result;
            }

            if (polish)
            {
                Polish(problem, layout, free, bestPositions, ref bestOccupancy, ref bestResult, options);
            }
            return Describe(problem, layout, bestPositions, bestOccupancy, bestResult);
        }

        /// <summary>
        /// 수렴한 배정을 자리 맞바꾸기로 마지막까지 밀어 본다. <see cref="Assign"/>의 비용이 직전
        /// 반복의 이웃을 보고 매겨지는 근사라, 이웃에 기대는 아티팩트가 섞이면 배정기 혼자서는
        /// 못 넘는 언덕이 생긴다 - 북향의 침 두 개를 한 아티팩트 아래로 쌓는 것 같은 수는 두
        /// 아티팩트가 동시에 움직여야 좋아지기 때문이다.
        ///
        /// 후보 하나를 재는 데는 다시 시뮬레이션하지 않는다. 받아들인 뒤에만 점유를 다시 짓고
        /// 시뮬레이션해, 다음 패스가 참값을 보게 한다. 그래서 비용은 (아티팩트 x 칸) 번의 채점
        /// 이고, 이긴 배치 하나에만 걸린다.
        /// </summary>
        private static void Polish(
            PlacementProblem problem, List<TabletPlacement> layout, List<GridPos> free,
            Dictionary<int, GridPos> positions, ref GridOccupancy occupancy, ref SimulationResult result,
            SolverOptions options)
        {
            if (positions.Count == 0 || free.Count == 0) return;

            var byCell = CharmsByCell(problem, positions);
            var score = ScoreOf(problem, layout, positions, occupancy, result, byCell);

            for (var pass = 0; pass < options.PolishPasses; pass++)
            {
                if (options.Cancellation.IsCancellationRequested) return;

                var moved = false;
                foreach (var charm in problem.Charms)
                {
                    if (!positions.TryGetValue(charm.InstanceId, out var from)) continue;

                    foreach (var to in free)
                    {
                        if (to == from) continue;

                        byCell.TryGetValue(to, out var occupant);
                        Move(positions, byCell, charm, from, occupant, to);

                        var trial = ScoreOf(problem, layout, positions, occupancy, result, byCell);
                        if (trial > score + StabilityBonus)
                        {
                            score = trial;
                            moved = true;
                            from = to;
                            continue;
                        }
                        Move(positions, byCell, charm, to, occupant, from);
                    }
                }
                if (!moved) return;

                // 받아들인 이동은 점유를 바꾼다. 조건 판정과 석판 조건이 그것을 보므로 다시
                // 시뮬레이션해야 다음 패스와 마지막 Describe 가 실제 배치의 값을 본다.
                occupancy = OccupancyFrom(layout, positions, problem);
                result = TabletSimulator.Run(
                    WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);
                score = ScoreOf(problem, layout, positions, occupancy, result, byCell);
            }
        }

        /// <summary>
        /// <paramref name="charm"/>을 <paramref name="to"/>로 옮기고, 거기 있던 것이 있으면 자리를
        /// 맞바꾼다. 되돌리기도 같은 호출이라 후보마다 사전을 새로 만들지 않는다.
        /// </summary>
        private static void Move(
            Dictionary<int, GridPos> positions, Dictionary<GridPos, CharmSlot> byCell,
            CharmSlot charm, GridPos from, CharmSlot? occupant, GridPos to)
        {
            positions[charm.InstanceId] = to;
            byCell[to] = charm;

            if (occupant is null) byCell.Remove(from);
            else
            {
                positions[occupant.InstanceId] = from;
                byCell[from] = occupant;
            }
        }

        /// <summary>
        /// <see cref="Describe"/>가 매기는 것과 같은 점수. 반복마다 견주기만 하면 되므로 격자와
        /// 목록까지 짓지 않는다 - 후보 배치마다 도는 자리라 그 할당이 그대로 GC 부담이 된다.
        /// </summary>
        private static double ScoreOf(
            PlacementProblem problem, List<TabletPlacement> layout, Dictionary<int, GridPos> positions,
            GridOccupancy occupancy, SimulationResult result, Dictionary<GridPos, CharmSlot>? neighbors)
        {
            var score = Familiarity(problem, layout);
            foreach (var charm in problem.Charms)
            {
                if (charm.IsFiller) continue;
                if (!positions.TryGetValue(charm.InstanceId, out var position)) continue;
                if (Reason(charm, position, result, problem.Grid, occupancy) != CharmInactiveReason.None) continue;

                score += Value(problem, charm, position, result, occupancy, neighbors);
            }
            return score;
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
            // 필러도 자리 유지·계획 유지 몫은 받아야 한다. 없으면 전 칸이 0점 동률이라 배정
            // 순서에 따라 필러끼리 자리를 맞바꾸는 제안이 나온다.
            if (charm.IsFiller) return Anchors(problem, charm, cell);

            // 꺼진 아티팩트도 마찬가지다. 0 만 돌려주면 전 칸이 동률이라 배정기가 풀 때마다
            // 아무 데나 보내고, 그 자리가 바뀌면 이웃을 보는 조건과 이웃 의존 가치가 따라 흔들려
            // 석판 배치의 점수까지 폴링마다 달라진다.
            if (Reason(charm, cell, result, problem.Grid, occupancy) != CharmInactiveReason.None)
                return Anchors(problem, charm, cell);

            var level = result.EffectiveLevel(cell, charm.Enchant);
            var effective = Math.Min(charm.Definition.MaxLevel, level);

            // 북향의 침은 강화할 대상이 있어야 제 몫을 한다. 대상 없이 선 침은 값어치가 0 이라,
            // 이것이 없으면 솔버가 침을 아무 데나 세우고도 최적이라고 한다.
            var factor = PositionalWorth.DependencyFactor(charm, cell, effective, neighbors);

            // 상한을 넘긴 레벨은 아무 값어치가 없다. 점수가 같은 배치라면 덜 흘리는 쪽을 고르도록
            // 아주 작은 차이만 준다. 실제 점수 차이를 뒤집을 만한 크기가 아니다.
            var value = charm.Weight * charm.Worth.At(effective) * factor
                        - WastePenalty * Math.Max(0, level - effective);

            if (charm.Definition.Behavior == "Charm_WhitePaper")
                value += WhitePaperWorth(problem, charm, cell, neighbors);

            if (charm.Definition.Behavior == "Charm_NearLevelDamage")
                value += NearLevelDamageWorth(charm, cell, effective, result, neighbors);

            // 자리가 대상을 정하는 것들. 무엇이 걸리는지는 정의가 답한다.
            value += PositionalWorth.InheritedComboWorth(problem, charm, cell, neighbors);
            value += PositionalWorth.NeighborEnhanceWorth(charm, cell, neighbors);
            value += PositionalWorth.RowCompanionWorth(charm, cell, problem.Grid, neighbors);
            value += PositionalWorth.LineCategoryWorth(problem, charm, cell);

            return value + Anchors(problem, charm, cell);
        }

        /// <summary>
        /// 점수가 같은 배치가 여럿일 때 지금 자리, 그다음 직전 제안의 자리를 지킨다. 채점할 때만
        /// 더하면 배정기가 이미 자리를 바꿔 놓은 뒤라, 이득이 없는데도 맞바꾸라는 제안이 나온다.
        /// </summary>
        private static double Anchors(PlacementProblem problem, CharmSlot charm, GridPos cell)
        {
            var value = 0.0;
            if (problem.CurrentCharms.TryGetValue(charm.InstanceId, out var current) && current == cell)
                value += StabilityBonus;
            if (problem.PlannedCharms.TryGetValue(charm.InstanceId, out var planned) && planned == cell)
                value += PlanBonus;
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

            if (result.IgnoreCriteriaAt(cell) > 0)
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
        /// 지금과 같은 자리에 있는 석판, 그다음 직전 제안과 같은 자리에 있는 석판마다 아주 작은
        /// 값을 더한다. 아티팩트 몫은 배정 단계에서 반영해야 뜻이 있어 <see cref="Anchors"/>가
        /// 따로 챙긴다.
        /// </summary>
        private static double Familiarity(PlacementProblem problem, List<TabletPlacement> layout)
        {
            var value = 0.0;

            for (var i = 0; i < layout.Count && i < problem.Tablets.Count; i++)
            {
                var id = problem.Tablets[i].InstanceId;
                if (problem.CurrentTablets.TryGetValue(id, out var spot) &&
                    spot.Position == layout[i].Position && spot.Rotation == layout[i].Rotation)
                {
                    value += StabilityBonus;
                }
                if (problem.PlannedTablets.TryGetValue(id, out var planned) &&
                    planned.Position == layout[i].Position && planned.Rotation == layout[i].Rotation)
                {
                    value += PlanBonus;
                }
            }
            return value;
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

            for (var i = 0; i < problem.Tablets.Count && i < layout.Count && i < result.Applied.Length; i++)
            {
                arrangement.AppliedTablets[problem.Tablets[i].InstanceId] = result.Applied[i];
                arrangement.TabletPositions[problem.Tablets[i].InstanceId] =
                    new TabletSpot(layout[i].Position, layout[i].Rotation);
            }

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
                if (result.IsDisabled(cell)) arrangement.DisabledCells.Add(cell);
            }
            return arrangement;
        }
    }
}
