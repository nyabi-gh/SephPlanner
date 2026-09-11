using System;
using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>석판 후보 탐색과 조건부 아티팩트 배정을 공통 우선순위로 비교한다.</summary>
    public static class PlacementSolver
    {
        private const double PlanBonus = 2e-8;

        /// <param name="layouts">
        /// 빔 탐색을 받아 올 자리. <see cref="ImproveTablets"/>가 private 이라 밖에서는 이 순서를
        /// 다시 엮을 수 없어 여기서 받는다.
        /// </param>
        public static Arrangement Solve(
            PlacementProblem problem, SolverOptions? options = null, LayoutCache? layouts = null)
        {
            options ??= new SolverOptions();
            var candidates = layouts is null ? SearchLayouts(problem, options) : layouts.Of(problem, options);
            return ImproveTablets(problem, EvaluateLayouts(problem, candidates, options), options);
        }

        internal static void CaptureProtectedActivation(PlacementProblem problem)
        {
            if (problem.InheritsActivationBaseline) return;
            problem.ProtectedActive.Clear();
            if (problem.CurrentCharms.Count != problem.Charms.Count ||
                !problem.Charms.All(charm => problem.CurrentCharms.ContainsKey(charm.InstanceId))) return;
            var layout = problem.Tablets.Count == 0 ? new List<TabletPlacement>() : Layout(problem, problem.CurrentTablets);
            if (layout is null) return;
            var current = Score(problem, layout, problem.CurrentCharms);
            foreach (var charm in problem.Charms)
                if (!charm.Retained && Preserve(charm) && !current.InactiveCharms.Contains(charm.InstanceId) &&
                    !current.UnlinkedCharms.Contains(charm.InstanceId)) problem.ProtectedActive.Add(charm.InstanceId);
        }

        private static Arrangement ImproveTablets(PlacementProblem problem, Arrangement best, SolverOptions options)
        {
            if (best.UnplacedTablets > 0 || problem.Tablets.Count == 0 || options.TabletRefinementTrials <= 0) return best;
            var cells = Cells(problem);
            var model = BuildEstimateModel(problem);
            var remaining = options.TabletRefinementTrials;
            for (var pass = 0; pass < options.PolishPasses && remaining > 0; pass++)
            {
                options.Cancellation.ThrowIfCancellationRequested();
                var candidates = new List<(List<TabletPlacement> Layout, PlacementQuality Quality)>();
                for (var index = 0; index < problem.Tablets.Count; index++)
                {
                    var slot = problem.Tablets[index];
                    var origin = best.Tablets[index];
                    var rotations = DistinctRotations(slot, CurrentRotation(problem, slot));
                    foreach (var cell in cells)
                    {
                        options.Cancellation.ThrowIfCancellationRequested();
                        foreach (var rotation in rotations)
                        {
                            if (cell == origin.Position && rotation == origin.Rotation) continue;
                            var trial = new List<TabletPlacement>(best.Tablets);
                            var other = trial.FindIndex(tablet => tablet.Position == cell);
                            if (other >= 0 && other != index)
                                trial[other] = problem.Tablets[other].At(origin.Position, trial[other].Rotation);
                            trial[index] = slot.At(cell, rotation);
                            candidates.Add((trial, Estimate(problem, cells, trial, model)));
                        }
                    }
                }
                var allowance = Math.Max(1, remaining / (options.PolishPasses - pass));
                var layouts = candidates.OrderByDescending(candidate => candidate.Quality)
                    .Take(allowance).Select(candidate => candidate.Layout).ToList();
                if (layouts.Count == 0) break;
                remaining -= layouts.Count;
                var improved = EvaluateLayouts(problem, layouts, options);
                if (PriorityComboPlacement.Compare(improved, best) <= 0) break;
                best = improved;
            }
            return best;
        }

        /// <summary>
        /// 채점할 석판 후보를 만든다. 같은 석판 구성의 조언들은 <see cref="LayoutCache"/>로 공유한다.
        /// </summary>
        public static List<List<TabletPlacement>> SearchLayouts(
            PlacementProblem problem, SolverOptions? options = null)
        {
            options ??= new SolverOptions();
            return WithCurrentAndPlanned(problem, SearchBeam(problem, options));
        }

        /// <summary>
        /// 빔이 찾아낸 것만. <b>캐시에 들어가는 것은 여기까지다.</b>
        ///
        /// 돌려주는 배치들은 석판 수가 서로 같다 - 한 석판을 놓는 단계마다 빔을 통째로 갈아
        /// 끼우므로, 놓을 자리가 모자라 도중에 멈추면 남은 것은 전부 같은 수까지만 놓은 배치다.
        /// <see cref="WithCurrentAndPlanned"/>가 완전한 배치만 고르는 근거다.
        /// </summary>
        internal static List<List<TabletPlacement>> SearchBeam(
            PlacementProblem problem, SolverOptions options)
        {
            CaptureProtectedActivation(problem);

            var cells = Cells(problem);
            var model = BuildEstimateModel(problem);
            return SearchTabletLayouts(problem, cells, options, model)
                .Take(options.ExactCandidates)
                .ToList();
        }

        /// <summary>
        /// 빔의 후보에 지금 배치와 직전 계획의 배치를 얹는다. 그 둘은 폴링마다 달라지므로
        /// 캐시에 담지 않고 부를 때마다 이 판에서 다시 짓는다.
        /// </summary>
        internal static List<List<TabletPlacement>> WithCurrentAndPlanned(
            PlacementProblem problem, List<List<TabletPlacement>> beam)
        {
            // 놓을 자리가 모자라면 탐색이 석판 일부를 뺀 배치를 내놓는다. 그런 배치를 그대로
            // 채점하면 존재하는 석판을 무시한 점수를 최적이라고 말하게 되므로, 완전한 배치가
            // 하나라도 있으면 불완전한 것은 버린다.
            var candidates = new List<List<TabletPlacement>>(beam.Count + 2);
            foreach (var layout in beam)
                if (layout.Count == problem.Tablets.Count) candidates.Add(layout);

            // 탐색이 현재 배치를 후보에서 떨어뜨리면, 이미 최적인 배치를 두고도 옮기라고 하게 된다.
            var asIs = Layout(problem, problem.CurrentTablets);
            if (asIs != null) candidates.Insert(0, asIs);

            // 직전 제안의 배치도 마찬가지다. 빔이 떨어뜨리면 같은 점수의 다른 배치로 갈아타
            // 따라가던 계획이 통째로 다시 쓰인다.
            var asPlanned = PlannedLayout(problem);
            if (asPlanned != null) candidates.Insert(0, asPlanned);

            // 완전한 배치가 아예 없으면(석판이 열린 칸보다 많은 극단) 놓을 수 있는 만큼이라도
            // 평가하되, Describe 가 빠진 수를 UnplacedTablets 로 남겨 호출자가 알 수 있게 한다.
            if (candidates.Count == 0) candidates.AddRange(beam);

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
            CaptureProtectedActivation(problem);

            var cells = Cells(problem);
            Arrangement? best = null;
            List<TabletPlacement>? bestLayout = null;
            var evaluated = new List<(List<TabletPlacement> Layout, Arrangement Result)>();
            foreach (var layout in layouts)
            {
                options.Cancellation.ThrowIfCancellationRequested();

                var arrangement = Evaluate(problem, cells, layout, options);
                evaluated.Add((layout, arrangement));
                if (best != null && PriorityComboPlacement.Compare(arrangement, best) <= 0) continue;

                best = arrangement;
                bestLayout = layout;
            }
            if (best is null) best = Evaluate(problem, cells, new List<TabletPlacement>(), options, polish: true);

            // 다듬기는 이긴 배치에만 건다. 후보마다 걸면 O(아티팩트^3)가 후보 수만큼 곱해져
            // 실시간 폴링이 못 따라온다 - 재어 보면 풀이 시간이 두 자릿수 배로 뛴다.
            if (bestLayout != null && options.PolishPasses > 0)
            {
                options.Cancellation.ThrowIfCancellationRequested();
                var polished = Evaluate(problem, cells, bestLayout, options, polish: true);
                if (PriorityComboPlacement.Compare(polished, best) > 0) best = polished;
            }
            best = ImproveEmptySides(problem, cells, evaluated, best, options);
            return PriorityComboPlacement.Improve(problem, best, options);
        }

        private static Arrangement ImproveEmptySides(
            PlacementProblem problem, List<GridPos> cells,
            List<(List<TabletPlacement> Layout, Arrangement Result)> evaluated, Arrangement best, SolverOptions options)
        {
            if (problem.Grid.Storage - problem.Tablets.Count - problem.Charms.Count < 2) return best;
            var budget = options.EmptySideTrials;
            foreach (var candidate in evaluated.OrderByDescending(pair => PlacementQuality.From(pair.Result)))
            {
                var occupied = new HashSet<GridPos>(candidate.Layout.Select(tablet => tablet.Position));
                foreach (var charm in problem.Charms.Where(c => !c.IsDormant && !c.IsFiller &&
                             c.Criteria == CharmCriteriaKind.BothSidesAreEmpty).OrderBy(c => c.InstanceId))
                {
                    foreach (var cell in cells.OrderByDescending(c => candidate.Result.CellLevels.TryGetValue(c, out var level) ? level : 0))
                    {
                        var empty = new HashSet<GridPos> { cell.Offset(-1, 0), cell.Offset(1, 0) };
                        if (cell.X <= 0 || cell.X >= problem.Grid.Width - 1 || occupied.Contains(cell) ||
                            empty.Any(c => !problem.Grid.Contains(c) || occupied.Contains(c))) continue;
                        if (budget-- <= 0) return best;
                        options.Cancellation.ThrowIfCancellationRequested();
                        var trial = Evaluate(problem, cells, candidate.Layout, options,
                            reservedEmpty: empty, forcedCharm: charm.InstanceId, forcedCell: cell);
                        if (PriorityComboPlacement.Compare(trial, best) > 0) best = trial;
                    }
                }
            }
            return best;
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

            public void Clear() => Array.Clear(_taken, 0, _taken.Length);

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
        /// 필수 조건과 레벨별 최대 가치 순으로 탐욕 배정할 입력. 음수·비단조 표도 보존한다.
        /// </summary>
        private sealed class EstimateModel
        {
            public int LevelCap;
            // 추정 후보 밖으로 반환하지 않는 작업 공간이며, 동시 실행하는 풀이는 각자 소유한다.
            public readonly EstimateOccupancy Occupancy;
            public readonly SimulationResult Simulation;

            public EstimateModel(GridSpec grid, bool anyMagic)
            {
                Occupancy = new EstimateOccupancy(grid, anyMagic);
                Simulation = new SimulationResult(grid, 0);
            }

            /// <summary>값어치 순위별, 레벨별 값어치. <c>[순위][레벨]</c>. 풀이마다 한 번만 짓는다.</summary>
            public double[][] ValueByRank = Array.Empty<double[]>();
            public CharmSlot[] Items = Array.Empty<CharmSlot>();
            public bool[] Required = Array.Empty<bool>(), Preserved = Array.Empty<bool>();
            public bool HasScales;
            public int[] Current = Array.Empty<int>(), Planned = Array.Empty<int>();
            public EstimateGroup[] Groups = Array.Empty<EstimateGroup>();
            public int[,] Members = new int[0, 0];
            public int[] GroupByCell = Array.Empty<int>();
            public bool[] Used = Array.Empty<bool>();
        }

        private struct EstimateGroup
        {
            public GridPos Cell;
            public int Level, Multiplier, Count, Head;
            public bool Disabled, Ignore, Unsafe;
        }

        /// <summary>
        /// 한 아티팩트가 그 레벨의 칸에서 갖는 값어치. <see cref="Value"/>가 매기는 것과 같은
        /// 잣대이되, 자리에 달린 몫(조건·이웃·안정)은 뺀 것이다 - 아직 어느 칸인지 모르기 때문이다.
        /// </summary>
        private static double RankValue(PlacementProblem problem, CharmSlot charm, int level) =>
            charm.Definition.ContextStats.Count > 0
                ? ContextStatWorth.Value(problem, charm, default, Math.Min(charm.Definition.MaxLevel, level), null, true) :
            charm.Definition.MagicSupport is not null
                ? DirectedCharmSupport.Estimate(problem, charm, level)
                : BuildStatWorth.Value(problem, charm, Math.Min(charm.Definition.MaxLevel, level));

        private static EstimateModel BuildEstimateModel(PlacementProblem problem)
        {
            var scoring = new List<CharmSlot>(problem.Charms.Count);
            var levelCap = 0;
            var anyMagic = false;

            foreach (var charm in problem.Charms)
            {
                if (charm.Definition.IsMagic) anyMagic = true;
                scoring.Add(charm);
                levelCap = Math.Max(levelCap, charm.Definition.MaxLevel);
            }
            if (scoring.Count == 0) levelCap = 5;

            var rows = new List<(CharmSlot Charm, double[] Values)>();
            foreach (var charm in scoring)
            {
                var row = new double[levelCap + 1];
                if (!charm.IsFiller && !charm.IsDormant)
                    for (var level = 0; level <= levelCap; level++) row[level] = RankValue(problem, charm, level);
                rows.Add((charm, row));
            }
            var ordered = rows.OrderByDescending(row => RequiresUse(problem, row.Charm))
                .ThenByDescending(row => row.Charm.Held).ThenByDescending(row => Preserve(row.Charm))
                .ThenByDescending(row => row.Values.Max()).ThenBy(row => row.Charm.InstanceId).ToList();
            return new EstimateModel(problem.Grid, anyMagic)
            {
                LevelCap = levelCap,
                HasScales = problem.Charms.Any(charm => ScalesPosition.Required(problem, charm)),
                Items = ordered.Select(row => row.Charm).ToArray(),
                ValueByRank = ordered.Select(row => row.Values).ToArray(),
                Required = ordered.Select(row => RequiresUse(problem, row.Charm)).ToArray(),
                Preserved = ordered.Select(row => Preserve(row.Charm)).ToArray(),
                Current = ordered.Select(row => AnchorIndex(problem, row.Charm, problem.CurrentCharms)).ToArray(),
                Planned = ordered.Select(row => AnchorIndex(problem, row.Charm, problem.PlannedCharms)).ToArray(),
                Groups = new EstimateGroup[problem.Grid.Storage],
                Members = new int[problem.Grid.Storage, problem.Grid.Storage],
                GroupByCell = new int[problem.Grid.Storage],
                Used = new bool[problem.Grid.Storage],
            };
        }

        private static int AnchorIndex(PlacementProblem problem, CharmSlot charm, Dictionary<int, GridPos> anchors) =>
            anchors.TryGetValue(charm.InstanceId, out var cell) && problem.Grid.Contains(cell)
                ? problem.Grid.ToIndex(cell.X, cell.Y) : -1;

        /// <summary>
        /// 직전 제안의 배치를 후보로 되살린다. 계획에 없는 새 석판이 끼면 계획된 자리는 그대로
        /// 두고 새 것만 남는 칸에서 탐욕으로 앉힌다 - 새 석판이 올 때마다 앵커가 통째로 사라지면,
        /// 정확히 개편이 가장 큰 그 순간에 계획이 다시 쓰인다(실측 녹화에서 그랬다).
        /// </summary>
        private static List<TabletPlacement>? PlannedLayout(PlacementProblem problem)
        {
            if (problem.Tablets.Count == 0 || problem.PlannedTablets.Count == 0) return null;

            var cells = Cells(problem);

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
            //
            // 어림 모델은 계획에서 빠진 석판이 있을 때만 짓는다. 빔을 돌려 쓰면 탐색을 건너뛰고도
            // 이 자리는 호출마다 지나가기 때문이다.
            EstimateModel? model = null;
            var layout = new List<TabletPlacement>(problem.Tablets.Count);
            foreach (var slot in problem.Tablets)
            {
                if (planned.TryGetValue(slot.InstanceId, out var spot))
                {
                    layout.Add(slot.At(spot.Position, spot.Rotation));
                    continue;
                }

                model ??= BuildEstimateModel(problem);
                var rotations = DistinctRotations(slot, CurrentRotation(problem, slot));

                List<TabletPlacement>? grown = null;
                PlacementQuality? bestScore = null;
                var bestCell = default(GridPos);
                foreach (var cell in cells)
                {
                    if (reserved.Contains(cell)) continue;
                    foreach (var rotation in rotations)
                    {
                        var trial = new List<TabletPlacement>(layout) { slot.At(cell, rotation) };
                        var estimate = Estimate(problem, cells, trial, model);
                        if (!bestScore.HasValue || estimate.CompareTo(bestScore.Value) > 0)
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
                options.Cancellation.ThrowIfCancellationRequested();

                // 돌릴 수 없는 석판은 지금 돌아가 있는 각도 그대로만 쓴다. 0으로 고정하면
                // 이미 돌아간 채로 잠긴 석판(저주 등)에 불가능한 회전을 제안하게 된다.
                var rotations = DistinctRotations(slot, CurrentRotation(problem, slot));
                // 같은 자리·회전의 석판은 탐색 중 읽기만 하므로 부모 후보들이 공유한다.
                var placements = cells.Select(cell => rotations.Select(rotation => slot.At(cell, rotation)).ToArray()).ToArray();

                var expanded = new List<(List<TabletPlacement> Layout, PlacementQuality Score, int Parent)>();

                for (var parent = 0; parent < beam.Count; parent++)
                {
                    var layout = beam[parent];
                    var taken = new HashSet<GridPos>(layout.Select(p => p.Position));

                    // 똑같은 석판끼리는 자리를 맞바꿔도 같은 배치다. 앞선 쌍둥이보다 뒤쪽 칸만
                    // 보게 해 그 순열들을 한 번씩만 만든다.
                    var floor = twin[index] >= 0 && twin[index] < layout.Count
                        ? problem.Grid.ToIndex(layout[twin[index]].Position.X, layout[twin[index]].Position.Y)
                        : -1;

                    for (var cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                    {
                        var cell = cells[cellIndex];
                        if (taken.Contains(cell)) continue;
                        if (floor >= 0 && problem.Grid.ToIndex(cell.X, cell.Y) <= floor) continue;

                        foreach (var placement in placements[cellIndex])
                        {
                            var next = new List<TabletPlacement>(layout.Count + 1);
                            next.AddRange(layout);
                            next.Add(placement);
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
            List<(List<TabletPlacement> Layout, PlacementQuality Score, int Parent)> expanded,
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
                var signature = string.Join("\u0001", new[]
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
        internal static int CurrentRotation(PlacementProblem problem, TabletSlot slot) =>
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
        /// 각 아티팩트가 남은 칸 중 가장 나은 칸을 고르는 빔 추정. 레벨 증가를 가치 증가로
        /// 가정하지 않는다. 이웃·지원 조건은 낙관적으로 보므로 최종 점수나 상한 보장은 아니다.
        /// </summary>
        private static PlacementQuality Estimate(
            PlacementProblem problem, List<GridPos> cells, List<TabletPlacement> layout, EstimateModel model)
        {
            var occupancy = model.Occupancy;
            occupancy.Clear();
            foreach (var placement in layout) occupancy.Take(placement.Position);
            var result = model.Simulation;
            TabletSimulator.RunInto(WithFixed(problem, layout), occupancy, result, problem.FixedEffects);

            var used = model.Used;
            var groups = model.Groups;
            var groupCount = 0;
            // 같은 효과의 칸은 레벨 표를 한 번만 비교한다. 남은 현재·직전 자리는 별도로 보존한다.
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                used[index] = !occupancy.HasCharm(cell);
                model.GroupByCell[index] = -1;
                if (used[index]) continue;
                var level = result.LevelAt(cell);
                var multiplier = result.MultiplierAt(cell);
                var disabled = result.IsDisabled(cell);
                var ignore = result.IgnoreCriteriaAt(cell) > 0;
                var group = 0;
                while (group < groupCount && (groups[group].Level != level || groups[group].Multiplier != multiplier ||
                    groups[group].Disabled != disabled || groups[group].Ignore != ignore ||
                    model.HasScales && ScalesPosition.IsLeft(groups[group].Cell) != ScalesPosition.IsLeft(cell))) group++;
                if (group == groupCount)
                {
                    groups[groupCount++] = new EstimateGroup
                    {
                        Cell = cell,
                        Level = level,
                        Multiplier = multiplier,
                        Disabled = disabled,
                        Ignore = ignore,
                        Unsafe = Unsafe(cell, result)
                    };
                }
                model.Members[group, groups[group].Count++] = index;
                model.GroupByCell[index] = group;
            }
            double total = 0, familiarity = Familiarity(problem, layout);
            int missing = 0, unheld = 0, unpreserved = 0, unsafeEmpty = 0, waste = 0;
            for (var rank = 0; rank < model.Items.Length; rank++)
            {
                var charm = model.Items[rank];
                var selected = -1;
                PlacementQuality? best = null;
                var current = model.Current[rank];
                var planned = model.Planned[rank];
                for (var group = 0; group < groupCount; group++)
                {
                    ref var entry = ref groups[group];
                    while (entry.Head < entry.Count && used[model.Members[group, entry.Head]]) entry.Head++;
                    if (entry.Head == entry.Count) continue;
                    var index = current >= 0 && !used[current] && model.GroupByCell[current] == group ? current :
                        planned >= 0 && !used[planned] && model.GroupByCell[planned] == group ? planned :
                        model.Members[group, entry.Head];
                    var cell = entry.Cell;
                    var level = result.EffectiveLevel(cell, charm.Enchant);
                    var active = !entry.Disabled && level >= 0 && !charm.IsDormant;
                    var held = !charm.Held || entry.Ignore;
                    var value = active ? model.ValueByRank[rank][Math.Min(level, model.LevelCap)] : 0;
                    var quality = new PlacementQuality((model.Required[rank] && !active ? 1 : 0) +
                        (ScalesPosition.Accepts(problem, charm, cell) ? 0 : 1),
                        model.Preserved[rank] && !active ? 1 : 0, held ? 0 : 1, 0, 0, value,
                        entry.Unsafe ? -1 : 0, charm.IsFiller || charm.IsDormant ? 0 : Math.Max(0, level - charm.Definition.MaxLevel),
                        (index == current ? 1 : 0) + (index == planned ? PlanBonus : 0), problem.Scale.ScoreStep);
                    if (best.HasValue)
                    {
                        var order = quality.CompareTo(best.Value);
                        if (order < 0 || order == 0 && index >= selected) continue;
                    }
                    best = quality;
                    selected = index;
                }
                if (!best.HasValue)
                {
                    if (RequiresUse(problem, charm)) missing++;
                    if (ScalesPosition.Required(problem, charm)) missing++;
                    if (charm.Held) unheld++;
                    if (Preserve(charm)) unpreserved++;
                    continue;
                }
                used[selected] = true;
                total += best.Value.Value;
                missing += best.Value.RetentionFailures;
                unheld += best.Value.HoldFailures;
                unpreserved += best.Value.ActivationFailures;
                waste += best.Value.Waste;
                familiarity += best.Value.Familiarity;
            }
            for (var index = 0; index < cells.Count; index++)
                if (!used[index] && groups[model.GroupByCell[index]].Unsafe) unsafeEmpty++;
            return new PlacementQuality(
                missing, unpreserved, unheld, 0, 0, total, unsafeEmpty, waste, familiarity, problem.Scale.ScoreStep);
        }

        private static Arrangement Evaluate(
            PlacementProblem problem, List<GridPos> cells, List<TabletPlacement> layout, SolverOptions options,
            bool polish = false, HashSet<GridPos>? reservedEmpty = null, int? forcedCharm = null, GridPos forcedCell = default)
        {
            var usable = reservedEmpty is null ? cells : cells.Where(cell => !reservedEmpty.Contains(cell)).ToList();
            var occupancy = OptimisticOccupancy(usable, layout, problem);
            var taken = new HashSet<GridPos>(layout.Select(p => p.Position));
            var free = usable.Where(cell => !taken.Contains(cell)).ToList();

            Dictionary<int, GridPos> positions = new Dictionary<int, GridPos>();
            Dictionary<GridPos, CharmSlot>? neighbors = null;
            SimulationResult result = TabletSimulator.Run(WithFixed(problem, layout), occupancy, problem.Grid, problem.FixedEffects);

            var bestPositions = positions;
            var bestOccupancy = occupancy;
            var bestResult = result;
            var bestScore = new PlacementQuality(int.MaxValue, int.MaxValue, int.MaxValue,
                0, 0, double.NegativeInfinity, int.MaxValue, int.MaxValue, 0, problem.Scale.ScoreStep);

            for (var iteration = 0; iteration < options.FixpointIterations; iteration++)
            {
                var next = Assign(problem, free, result, occupancy, neighbors, forcedCharm, forcedCell);
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
                if (score.CompareTo(bestScore) <= 0) continue;

                bestScore = score;
                bestPositions = positions;
                bestOccupancy = occupancy;
                bestResult = result;
            }

            var current = problem.CurrentCharms;
            if (problem.Charms.All(charm => current.ContainsKey(charm.InstanceId)) &&
                current.Count == problem.Charms.Count && current.Values.Distinct().Count() == current.Count &&
                current.Values.All(free.Contains) &&
                (!forcedCharm.HasValue || current[forcedCharm.Value] == forcedCell))
            {
                var currentOccupancy = OccupancyFrom(layout, current, problem);
                var currentResult = TabletSimulator.Run(WithFixed(problem, layout), currentOccupancy, problem.Grid, problem.FixedEffects);
                var currentScore = ScoreOf(problem, layout, current, currentOccupancy, currentResult, CharmsByCell(problem, current));
                if (currentScore.CompareTo(bestScore) > 0)
                {
                    bestPositions = new Dictionary<int, GridPos>(current);
                    bestOccupancy = currentOccupancy;
                    bestResult = currentResult;
                }
            }
            if (polish)
            {
                Polish(problem, layout, free, bestPositions, ref bestOccupancy, ref bestResult, options);
            }
            else if (problem.Charms.Any(charm => (charm.Retained || Preserve(charm)) && DirectedCharmSupport.HasConnection(charm)))
            {
                for (var pass = 0; pass < options.PolishPasses; pass++)
                {
                    var bestNeighbors = CharmsByCell(problem, bestPositions);
                    if (!problem.Charms.Any(charm => (charm.Retained || Preserve(charm)) && DirectedCharmSupport.HasConnection(charm) &&
                        (!bestPositions.TryGetValue(charm.InstanceId, out var position) ||
                         !CanUse(problem, charm, position, bestResult, bestOccupancy, bestNeighbors)))) break;
                    if (!PolishSupportPairs(problem, layout, free, bestPositions, ref bestOccupancy,
                        ref bestResult, options, forcedCharm, forcedCell)) break;
                }
            }
            return Describe(problem, layout, bestPositions, bestOccupancy, bestResult);
        }

        /// <summary>
        /// 수렴한 배정을 자리 맞바꾸기로 마지막까지 밀어 본다. <see cref="Assign"/>의 비용이 직전
        /// 반복의 이웃을 보고 매겨지는 근사라, 이웃에 기대는 아티팩트가 섞이면 배정기 혼자서는
        /// 못 넘는 언덕이 생긴다 - 북향의 침 두 개를 한 아티팩트 아래로 쌓는 것 같은 수는 두
        /// 아티팩트가 동시에 움직여야 좋아지기 때문이다.
        ///
        /// 교환 후보의 점유와 석판 효과를 다시 계산한 뒤 받아들인다. 이전 배치의 조건으로
        /// 채점하면 조건부 석판·아티팩트의 활성 상태와 사용 유지 조건을 잘못 판단한다.
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
                options.Cancellation.ThrowIfCancellationRequested();

                var moved = false;
                foreach (var charm in problem.Charms)
                {
                    if (!positions.TryGetValue(charm.InstanceId, out var from)) continue;

                    foreach (var to in free)
                    {
                        if (to == from) continue;

                        byCell.TryGetValue(to, out var occupant);
                        Move(positions, byCell, charm, from, occupant, to);

                        var trialOccupancy = OccupancyFrom(layout, positions, problem);
                        var trialResult = TabletSimulator.Run(WithFixed(problem, layout), trialOccupancy, problem.Grid, problem.FixedEffects);
                        var trial = ScoreOf(problem, layout, positions, trialOccupancy, trialResult, byCell);
                        if (trial.CompareTo(score) > 0)
                        {
                            occupancy = trialOccupancy;
                            result = trialResult;
                            score = trial;
                            moved = true;
                            from = to;
                            continue;
                        }
                        Move(positions, byCell, charm, to, occupant, from);
                    }
                }
                if (PolishSupportPairs(problem, layout, free, positions, ref occupancy, ref result, options))
                {
                    moved = true;
                    byCell = CharmsByCell(problem, positions);
                    score = ScoreOf(problem, layout, positions, occupancy, result, byCell);
                }
                if (!moved) return;
            }
        }

        private static bool PolishSupportPairs(
            PlacementProblem problem, List<TabletPlacement> layout, List<GridPos> free,
            Dictionary<int, GridPos> positions, ref GridOccupancy occupancy, ref SimulationResult result,
            SolverOptions options, int? forcedCharm = null, GridPos forcedCell = default)
        {
            var helpers = problem.Charms.Where(charm => !charm.IsFiller && !charm.IsDormant &&
                DirectedCharmSupport.HasConnection(charm) && positions.ContainsKey(charm.InstanceId)).ToList();
            if (helpers.Count == 0) return false;
            var targets = problem.Charms.Where(charm => !charm.IsFiller && !charm.IsDormant &&
                positions.ContainsKey(charm.InstanceId)).ToList();
            if (targets.Count == 0) return false;
            var available = new HashSet<GridPos>(free);
            var currentNeighbors = CharmsByCell(problem, positions);
            var score = ScoreOf(problem, layout, positions, occupancy, result, currentNeighbors);
            var moved = false;
            foreach (var helper in helpers)
            {
                var support = DirectedCharmSupport.Offset(helper);
                foreach (var target in targets)
                {
                    if (!DirectedCharmSupport.Accepts(helper, target)) continue;
                    HashSet<CharmSlot>? connected = null;
                    foreach (var cell in free)
                    {
                        options.Cancellation.ThrowIfCancellationRequested();
                        var targetCell = cell.Offset(support.X, support.Y);
                        if (cell == targetCell || !available.Contains(targetCell)) continue;
                        if (positions[helper.InstanceId] == cell && positions[target.InstanceId] == targetCell) continue;
                        if (connected is null)
                        {
                            connected = new HashSet<CharmSlot> { target };
                            bool expanded;
                            do
                            {
                                expanded = false;
                                foreach (var other in helpers)
                                {
                                    if (other == helper) continue;
                                    var direction = DirectedCharmSupport.Offset(other);
                                    var adjacent = positions[other.InstanceId].Offset(direction.X, direction.Y);
                                    if (!currentNeighbors.TryGetValue(adjacent, out var existingTarget) ||
                                        existingTarget == helper || !DirectedCharmSupport.Accepts(other, existingTarget)) continue;
                                    if (!connected.Contains(other) && !connected.Contains(existingTarget)) continue;
                                    expanded |= connected.Add(other);
                                    expanded |= connected.Add(existingTarget);
                                }
                            } while (expanded);
                        }
                        var destinations = new Dictionary<int, GridPos> { [target.InstanceId] = targetCell };
                        foreach (var other in connected)
                        {
                            var previous = positions[other.InstanceId];
                            var origin = positions[target.InstanceId];
                            destinations[other.InstanceId] = targetCell.Offset(previous.X - origin.X, previous.Y - origin.Y);
                        }
                        connected.Add(helper);
                        destinations[helper.InstanceId] = cell;
                        if (destinations.Values.Any(destination => !available.Contains(destination)) ||
                            destinations.Values.Distinct().Count() != destinations.Count) continue;
                        // 연결된 사슬의 상대 위치를 보존하고, 밀려난 아이템은 교환으로 보존한다.
                        var trial = new Dictionary<int, GridPos>(positions);
                        var neighbors = CharmsByCell(problem, trial);
                        foreach (var other in connected)
                        {
                            var destination = destinations[other.InstanceId];
                            neighbors.TryGetValue(destination, out var displaced);
                            Move(trial, neighbors, other, trial[other.InstanceId], displaced, destination);
                        }
                        if (forcedCharm.HasValue && trial[forcedCharm.Value] != forcedCell) continue;
                        var trialOccupancy = OccupancyFrom(layout, trial, problem);
                        var trialResult = TabletSimulator.Run(WithFixed(problem, layout), trialOccupancy, problem.Grid, problem.FixedEffects);
                        if (!CanUse(problem, helper, trial[helper.InstanceId], trialResult, trialOccupancy, neighbors)) continue;
                        if (connected.Any(other => DirectedCharmSupport.HasConnection(other) &&
                            !DirectedCharmSupport.IsConnected(other, trial[other.InstanceId], trialResult,
                                problem.Grid, trialOccupancy, neighbors))) continue;
                        var quality = ScoreOf(problem, layout, trial, trialOccupancy, trialResult, neighbors);
                        if (quality.CompareTo(score) <= 0) continue;
                        positions.Clear();
                        foreach (var pair in trial) positions.Add(pair.Key, pair.Value);
                        currentNeighbors = neighbors;
                        connected = null;
                        occupancy = trialOccupancy;
                        result = trialResult;
                        score = quality;
                        moved = true;
                    }
                }
            }
            return moved;
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
        /// <see cref="Describe"/>가 매기는 <see cref="Arrangement.Preference"/>와 같은 값. 반복마다
        /// 견주기만 하면 되므로 격자와 목록까지 짓지 않는다 - 후보 배치마다 도는 자리라 그 할당이
        /// 그대로 GC 부담이 된다.
        /// </summary>
        private static PlacementQuality ScoreOf(
            PlacementProblem problem, List<TabletPlacement> layout, Dictionary<int, GridPos> positions,
            GridOccupancy occupancy, SimulationResult result, Dictionary<GridPos, CharmSlot>? neighbors)
        {
            var familiarity = Familiarity(problem, layout);
            double score = 0;
            int missing = 0, unpreserved = 0, unheld = 0, waste = 0, unsafeEmpty = 0;
            var combo = problem.PriorityCategories.Count == 0
                ? null
                : new Arrangement { ScoreStep = problem.Scale.ScoreStep };
            foreach (var charm in problem.Charms)
            {
                if (!positions.TryGetValue(charm.InstanceId, out var position))
                {
                    if (RequiresUse(problem, charm) && !charm.IsFiller) missing++;
                    if (ScalesPosition.Required(problem, charm)) missing++;
                    if (Preserve(charm)) unpreserved++;
                    if (charm.Held && !charm.IsFiller) unheld++;
                    continue;
                }
                if (!ScalesPosition.Accepts(problem, charm, position)) missing++;
                if (!CanUse(problem, charm, position, result, occupancy, neighbors))
                {
                    if (RequiresUse(problem, charm) && !charm.IsFiller) missing++;
                    if (Preserve(charm)) unpreserved++;
                }
                if (charm.Held && !charm.IsFiller && result.IgnoreCriteriaAt(position) <= 0) unheld++;
                score += Value(problem, charm, position, result, occupancy, neighbors);
                familiarity += Anchors(problem, charm, position);
                waste += Waste(charm, position, result);
                if (combo is not null)
                {
                    combo.CharmPositions[charm.InstanceId] = position;
                    if (Reason(charm, position, result, problem.Grid, occupancy) != CharmInactiveReason.None)
                        combo.InactiveCharms.Add(charm.InstanceId);
                }
            }
            for (var index = 0; index < problem.Grid.Storage; index++)
            {
                var cell = problem.Grid.ToPosition(index);
                if (!occupancy.HasItem(cell) && Unsafe(cell, result)) unsafeEmpty++;
            }
            if (combo is not null && neighbors is not null) PriorityComboPlacement.Describe(problem, combo, neighbors);
            return new PlacementQuality(missing, unpreserved, unheld, combo?.PriorityComboMatches ?? 0,
                combo?.PriorityComboProgress ?? 0, score, unsafeEmpty, waste, familiarity, problem.Scale.ScoreStep);
        }

        internal static bool Preserve(CharmSlot charm) => !charm.IsFiller && !charm.IsDormant && !charm.Definition.HasNoActivationEffect && !charm.AllowDeactivation;
        private static bool RequiresUse(PlacementProblem problem, CharmSlot charm) =>
            charm.Retained || problem.ProtectedActive.Contains(charm.InstanceId);

        private static bool Unsafe(GridPos cell, SimulationResult result) =>
            result.IsDisabled(cell) || result.EffectiveLevel(cell, 0) < 0;

        private static int Waste(CharmSlot charm, GridPos cell, SimulationResult result) =>
            charm.IsFiller || charm.IsDormant ? 0 : Math.Max(0, result.EffectiveLevel(cell, charm.Enchant) - charm.Definition.MaxLevel);

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
            Dictionary<GridPos, CharmSlot>? neighbors, int? forcedCharm = null, GridPos forcedCell = default)
        {
            var positions = new Dictionary<int, GridPos>();
            if (problem.Charms.Count == 0 || free.Count == 0) return positions;

            var charmsAreRows = problem.Charms.Count <= free.Count;
            var rows = charmsAreRows ? problem.Charms.Count : free.Count;
            var columns = charmsAreRows ? free.Count : problem.Charms.Count;
            var cost = new AssignmentCost[rows, columns];
            var unit = rows + 1.0;

            for (var charmIndex = 0; charmIndex < problem.Charms.Count; charmIndex++)
            {
                for (var cellIndex = 0; cellIndex < free.Count; cellIndex++)
                {
                    // 헝가리안은 비용을 최소화하므로 점수를 뒤집어 넣는다. 자리를 지키는 몫은
                    // 여기서 더해야 뜻이 있다 - 채점할 때만 더하면 배정기가 이미 자리를 바꿔 놓은
                    // 뒤라, 이득이 없는데도 맞바꾸라는 제안이 나온다.
                    var charm = problem.Charms[charmIndex];
                    var cell = free[cellIndex];
                    var usable = CanUse(problem, charm, cell, result, occupancy, neighbors);
                    var priority = 0.0;
                    // 하위 조건의 전체 위반 수보다 큰 기수로 강제 배치·사용 유지·고정·활성을 순서대로 비교한다.
                    if (RequiresUse(problem, charm) && !charm.IsFiller && usable) priority -= unit * unit;
                    if (charm.Held && !charm.IsFiller && result.IgnoreCriteriaAt(cell) > 0) priority -= unit;
                    if (Preserve(charm) && usable) priority--;
                    if (!ScalesPosition.Accepts(problem, charm, cell)) priority += unit * unit;
                    var row = charmsAreRows ? charmIndex : cellIndex;
                    var column = charmsAreRows ? cellIndex : charmIndex;
                    if (forcedCharm.HasValue &&
                        ((charm.InstanceId == forcedCharm.Value) != (free[cellIndex] == forcedCell)))
                        priority += 2 * unit * unit * unit;
                    cost[row, column] = new AssignmentCost(priority,
                        -Value(problem, charm, cell, result, occupancy, neighbors),
                        Unsafe(cell, result) ? -1 : 0, Waste(charm, cell, result), -Anchors(problem, charm, cell));
                }
            }

            var assignment = HungarianAssignment.Solve(cost);
            for (var row = 0; row < assignment.Length; row++)
            {
                if (assignment[row] < 0) continue;

                var charmIndex = charmsAreRows ? row : assignment[row];
                var cellIndex = charmsAreRows ? assignment[row] : row;
                positions[problem.Charms[charmIndex].InstanceId] = free[cellIndex];
            }
            return positions;
        }

        /// <summary>
        /// 이 아티팩트가 그 칸에서 갖는 값어치. 보고되는 점수는 이것의 합이다. 자리를 지키는 몫은
        /// 여기 없고 <see cref="Anchors"/>가 따로 매긴다 - 점수에 섞으면 안 되기 때문이다.
        /// </summary>
        private static double Value(
            PlacementProblem problem, CharmSlot charm, GridPos cell,
            SimulationResult result, GridOccupancy occupancy, Dictionary<GridPos, CharmSlot>? neighbors)
        {
            if (charm.IsFiller) return 0;
            var inactive = Reason(charm, cell, result, problem.Grid, occupancy) != CharmInactiveReason.None;
            if (inactive && !PositionalWorth.IsNeedle(charm.Definition))
                return DormantPreference(problem, charm, cell, result, occupancy);

            var level = result.EffectiveLevel(cell, charm.Enchant);
            var effective = inactive ? 0 : Math.Min(charm.Definition.MaxLevel, level);
            if (charm.Definition.MagicSupport is not null)
                return DirectedCharmSupport.Value(problem, charm, cell, effective, result, occupancy, neighbors);

            // 북향의 침은 강화할 대상이 있어야 제 몫을 한다. 대상 없이 선 침은 값어치가 0 이라,
            // 이것이 없으면 솔버가 침을 아무 데나 세우고도 최적이라고 한다.
            var factor = PositionalWorth.DependencyFactor(charm, cell, effective, neighbors);
            if (PositionalWorth.IsNeedle(charm.Definition) && neighbors is not null &&
                !DirectedCharmSupport.IsConnected(charm, cell, result, problem.Grid, occupancy, neighbors)) factor = 0;
            // DisableEffect는 침의 보너스 요청을 끄지 않고 limitedEffectEnabledLevel만 0으로 만든다.
            if (inactive) return charm.Worth.WeightedAt(0, charm.Weight) * factor;

            var value = BuildStatWorth.Value(problem, charm, effective, result, occupancy, neighbors) * factor;
            if (charm.Definition.ContextStats.Count > 0)
                value = ContextStatWorth.Value(problem, charm, cell, effective, neighbors, result: result, occupancy: occupancy);

            if (charm.Definition.Behavior == "Charm_NearLevelDamage")
                value += CharmWorth.ApplyWeight(
                    NearLevelDamageWorth(problem, charm, cell, effective, result, neighbors), charm.Weight);

            // 자리가 대상을 정하는 것들. 무엇이 걸리는지는 정의가 답한다.
            value += CharmWorth.ApplyWeight(PositionalWorth.ComboWorth(problem, charm, cell, neighbors), charm.Weight);
            value += CharmWorth.ApplyWeight(PositionalWorth.NeighborEnhanceWorth(charm, cell, neighbors), charm.Weight);
            value += CharmWorth.ApplyWeight(PositionalWorth.RowCompanionWorth(charm, cell, problem.Grid, neighbors), charm.Weight);

            return value;
        }

        /// <summary>
        /// 필수 조건·효과·감점·초과 강화가 같을 때 현재 자리, 직전 제안 순으로 유지한다.
        /// 필러와 꺼진 아티팩트도 받는다 - 값어치가 0 이라 전 칸이 동률이면 배정기가 풀 때마다
        /// 아무 데나 보내고, 그 자리가 바뀌면 이웃을 보는 조건과 이웃 의존 가치가 따라 흔들려
        /// 석판 배치의 점수까지 폴링마다 달라진다.
        /// </summary>
        private static double Anchors(PlacementProblem problem, CharmSlot charm, GridPos cell)
        {
            var value = 0.0;
            if (problem.CurrentCharms.TryGetValue(charm.InstanceId, out var current) && current == cell)
                value += 1;
            if (problem.PlannedCharms.TryGetValue(charm.InstanceId, out var planned) && planned == cell)
                value += PlanBonus;
            return value;
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
            PlacementProblem problem, CharmSlot charm, GridPos cell, int ownLevel,
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
            return Math.Floor(perLevel * sum) * problem.Scale.DamageBonus;
        }

        private static bool CanUse(
            PlacementProblem problem, CharmSlot charm, GridPos cell, SimulationResult result,
            GridOccupancy occupancy, IReadOnlyDictionary<GridPos, CharmSlot>? neighbors) =>
            Reason(charm, cell, result, problem.Grid, occupancy) == CharmInactiveReason.None &&
            (!DirectedCharmSupport.HasConnection(charm) ||
             DirectedCharmSupport.IsConnected(charm, cell, result, problem.Grid, occupancy, neighbors));

        /// <summary>
        /// <summary>
        /// 연동 무기를 안 들어 꺼져 있는데 사용자가 강화 우선을 지정한 아티팩트의 자리 값어치.
        ///
        /// 꺼진 아티팩트는 지금 어느 칸에서도 주는 것이 없으므로 배수를 곱해도 0 이고, 그래서
        /// 별을 셋 줘도 남는 칸으로 밀렸다. 그런데 <b>별은 우리가 추정한 값이 아니라 사용자가
        /// 넣은 바깥 정보다</b> - 꺼진 것에 굳이 별을 주는 이유는 무기를 바꿀 생각이기 때문이다.
        /// 그래서 무기를 바꿨을 때 받게 될 값어치로 자리를 다투게 한다.
        ///
        /// <b>이 값은 점수에 넣지 않는다</b>(<see cref="Earned"/>). 지금 실제로 받는 것은 여전히
        /// 0 이고, 화면 점수가 그 사실을 말해야 한다. 자리만 잡아 주고 점수는 정직하게 둔다.
        ///
        /// 칸이 죽어 있거나 배치 조건을 못 맞추면 0 이다 - 무기를 바꿔도 켜지지 않는 자리다.
        /// </summary>
        private static double DormantPreference(
            PlacementProblem problem, CharmSlot charm, GridPos cell,
            SimulationResult result, GridOccupancy occupancy)
        {
            if (!charm.IsDormant || charm.Weight <= 1 || charm.IsFiller) return 0;
            if (ReasonIgnoringWeapon(charm, cell, result, problem.Grid, occupancy) != CharmInactiveReason.None)
                return 0;

            var level = Math.Max(0, Math.Min(charm.Definition.MaxLevel, result.EffectiveLevel(cell, charm.Enchant)));
            return charm.Worth.WeightedAt(level, charm.Weight);
        }

        /// <summary>화면에 나가는 점수. 자리 선호로만 쓰는 몫은 빼고 실제로 받는 것만 센다.</summary>
        private static double Earned(
            PlacementProblem problem, CharmSlot charm, GridPos cell,
            SimulationResult result, GridOccupancy occupancy, Dictionary<GridPos, CharmSlot>? neighbors) =>
            Value(problem, charm, cell, result, occupancy, neighbors)
            - DormantPreference(problem, charm, cell, result, occupancy);

        /// 효과가 꺼졌다면 그 이유. 게임의 <c>Charm_Basic.RefreshCharm</c>이 보는 조건과 같고,
        /// 자리를 옮겨서는 풀 수 없는 무기 불일치를 먼저 본다.
        /// </summary>
        internal static CharmInactiveReason Reason(
            CharmSlot charm, GridPos cell, SimulationResult result, GridSpec grid, GridOccupancy occupancy)
        {
            if (charm.IsDormant) return CharmInactiveReason.Weapon;
            return ReasonIgnoringWeapon(charm, cell, result, grid, occupancy);
        }

        /// <summary>무기를 뺀 나머지 조건. 무기를 바꾸면 켜질 자리인지 보는 데 쓴다.</summary>
        private static CharmInactiveReason ReasonIgnoringWeapon(
            CharmSlot charm, GridPos cell, SimulationResult result, GridSpec grid, GridOccupancy occupancy)
        {
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
        /// 지금과 같은 자리·각도에 있는 석판 수와, 직전 제안과 같은 자리에 있는
        /// 석판마다 아주 작은 값을 더한다. 회전만 바뀌는 것도 한 수다. 아티팩트 몫은 배정 단계에서
        /// 반영해야 뜻이 있어 <see cref="Anchors"/>가 따로 챙긴다.
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
                    value += 1;
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
            var arrangement = new Arrangement { ScoreStep = problem.Scale.ScoreStep };
            arrangement.Tablets.AddRange(layout);
            arrangement.UnplacedTablets = problem.Tablets.Count - layout.Count;
            arrangement.Preference = Familiarity(problem, layout);

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
                    if (charm.Held && !charm.IsFiller) arrangement.UnheldCharms.Add(charm.InstanceId);
                    continue;
                }

                arrangement.CharmPositions[charm.InstanceId] = position;
                arrangement.Preference += Anchors(problem, charm, position);
                if (charm.Held && !charm.IsFiller && result.IgnoreCriteriaAt(position) <= 0)
                    arrangement.UnheldCharms.Add(charm.InstanceId);

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
                    var residual = Earned(problem, charm, position, result, occupancy, neighbors);
                    arrangement.Score += residual;
                    continue;
                }

                arrangement.EffectiveLevels[position] = Math.Max(0, Math.Min(charm.Definition.MaxLevel, level));
                var value = Earned(problem, charm, position, result, occupancy, neighbors);
                arrangement.Score += value;
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
            foreach (var charm in problem.Charms)
            {
                if (charm.IsFiller) continue;
                if (!arrangement.CharmPositions.TryGetValue(charm.InstanceId, out var actualPosition) ||
                    !ScalesPosition.Accepts(problem, charm, actualPosition))
                {
                    if (ScalesPosition.Required(problem, charm)) arrangement.WrongSideCharms.Add(charm.InstanceId);
                }
                var inactive = arrangement.InactiveCharms.Contains(charm.InstanceId);
                var unlinked = !inactive && DirectedCharmSupport.HasConnection(charm) &&
                    !DirectedCharmSupport.IsConnected(charm, positions[charm.InstanceId], result, problem.Grid,
                        occupancy, neighbors);
                if (unlinked) arrangement.UnlinkedCharms.Add(charm.InstanceId);
                if (charm.Retained && (inactive || unlinked)) arrangement.UnretainedCharms.Add(charm.InstanceId);
                if (Preserve(charm) && (inactive || unlinked)) arrangement.UnpreservedCharms.Add(charm.InstanceId);
                if (!charm.Retained && problem.ProtectedActive.Contains(charm.InstanceId) && (inactive || unlinked))
                    arrangement.UnapprovedDeactivations.Add(charm.InstanceId);
            }
            PriorityComboPlacement.Describe(problem, arrangement, neighbors);
            var quality = ScoreOf(problem, layout, positions, occupancy, result, neighbors);
            arrangement.UnsafeEmptyCells = quality.UnsafeEmpty;
            arrangement.WastedLevels = quality.Waste;
            return arrangement;
        }
    }
}
