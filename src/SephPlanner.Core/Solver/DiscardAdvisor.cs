using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    public sealed class DiscardAdvice
    {
        public int InstanceId { get; set; }
        public string Name { get; set; } = "";
        public bool IsTablet { get; set; }
        public GridPos Position { get; set; }
        public double Gain { get; set; }
        public List<string> Activated { get; set; } = new List<string>();
        public bool ReducesComboCount { get; set; }
    }

    /// <summary>하나를 제외하는 가상 배치만 비교한다. 실제 제거 명령이나 자동 배치 목표는 만들지 않는다.</summary>
    public static class DiscardAdvisor
    {
        /// <param name="layouts">
        /// 배치 탐색을 나눠 쓸 자리. 제거 후보는 석판을 밀어내는 갈래에서만 구성이 달라지고,
        /// 나머지 마흔 갈래는 지금 구성 그대로다. 여기가 캐시를 안 받고 직접 탐색하던 동안
        /// <b>석판 13장짜리 판에서 재계산 650ms 중 490ms</b>가 그 한 번의 탐색이었다.
        /// </param>
        public static List<DiscardAdvice> Rank(PlacementProblem problem, Arrangement baseline,
            LayoutCache? layouts = null, CancellationToken cancellation = default)
        {
            var advice = new List<DiscardAdvice>();
            if (problem.Charms.Count == 0 || baseline.UnplacedTablets > 0) return advice;
            cancellation.ThrowIfCancellationRequested();

            layouts ??= new LayoutCache();
            var options = SolverOptions.ForAdvice(cancellation);
            options.EmptySideTrials = 12;
            options.PriorityComboTrials = 24;
            // 제거 후보마다 빔을 다시 만들지 않는다. 현재·제안 배치를 포함한 공통 후보 위에서 비교한다.
            var candidateLayouts = new List<List<TabletPlacement>> { baseline.Tablets };
            candidateLayouts.AddRange(layouts.Of(problem, options).Take(4));
            var currentCounts = Counts(problem, problem.CurrentCharms);
            var baselineCounts = Counts(problem, baseline.CharmPositions);
            var candidates = problem.Charms.Select(c => (c.InstanceId, Tablet: false))
                .Concat(problem.Tablets.Select(t => (t.InstanceId, Tablet: true))).OrderBy(c => c.InstanceId);

            foreach (var candidate in candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                var trial = OfferAdvisor.Clone(problem);
                string name;
                var yardstick = candidateLayouts;
                if (candidate.Tablet)
                {
                    var index = trial.Tablets.FindIndex(t => t.InstanceId == candidate.InstanceId);
                    var tablet = trial.Tablets[index];
                    name = Naming.OfTablet(tablet.At(default, 0));
                    trial.Tablets.RemoveAt(index);
                    trial.CurrentTablets.Remove(candidate.InstanceId);
                    trial.PlannedTablets.Remove(candidate.InstanceId);
                    yardstick = candidateLayouts.Where(l => l.Count == problem.Tablets.Count)
                        .Select(l => l.Where((_, i) => i != index).ToList()).ToList();
                }
                else
                {
                    var charm = trial.Charms.First(c => c.InstanceId == candidate.InstanceId);
                    if (charm.IsFiller || charm.Held || charm.Retained || charm.Definition.CannotDiscard) continue;
                    name = Naming.Of(charm.Definition.Names, charm.Definition.Id, "아티팩트");
                    trial.Charms.Remove(charm);
                    trial.CurrentCharms.Remove(candidate.InstanceId);
                    trial.PlannedCharms.Remove(candidate.InstanceId);
                }

                var remainingCounts = Counts(trial, trial.CurrentCharms);
                trial.ComboCounts = Adjust(problem.ComboCounts, currentCounts, remainingCounts);
                var solved = PlacementSolver.EvaluateLayouts(trial, yardstick, options);
                cancellation.ThrowIfCancellationRequested();
                if (!ActivationPolicy.AllowsTransition(baseline, solved) || solved.UnretainedCharms.Count > 0 || solved.UnplacedTablets > 0 || solved.CharmPositions.Count != trial.Charms.Count ||
                    solved.Score <= baseline.Score + 0.001 || PriorityPlacement.Compare(solved, baseline) <= 0)
                    continue;

                var finalCounts = Counts(trial, solved.CharmPositions);
                var before = Adjust(problem.ComboCounts, currentCounts, baselineCounts);
                var after = Adjust(problem.ComboCounts, currentCounts, finalCounts);
                // 고정 콤보의 전투 효과는 배치 점수에 없으므로, 단계가 달라지는 경우를 이득으로 단정하지 않는다.
                if (before.Keys.Union(after.Keys).Any(key =>
                    {
                        var combo = problem.Combos?.Invoke(key);
                        return combo is null ? Count(before, key) != Count(after, key) : combo.Thresholds.Any(t =>
                            (Count(before, key) >= t) != (Count(after, key) >= t));
                    })) continue;

                advice.Add(new DiscardAdvice
                {
                    InstanceId = candidate.InstanceId,
                    Name = name,
                    IsTablet = candidate.Tablet,
                    Position = candidate.Tablet ? problem.CurrentTablets[candidate.InstanceId].Position :
                        problem.CurrentCharms[candidate.InstanceId],
                    Gain = solved.Score - baseline.Score,
                    ReducesComboCount = before.Any(p => Count(after, p.Key) < p.Value),
                    Activated = trial.Charms.Where(c => baseline.InactiveCharms.Contains(c.InstanceId) &&
                                                       !solved.InactiveCharms.Contains(c.InstanceId))
                        .Select(c => Naming.Of(c.Definition.Names, c.Definition.Id, "아티팩트")).ToList(),
                });
            }
            return advice.OrderByDescending(a => a.Gain).ThenBy(a => a.InstanceId).Take(3).ToList();
        }

        private static Dictionary<string, int> Counts(PlacementProblem problem, IReadOnlyDictionary<int, GridPos> positions) =>
            ComboCounting.CountAll(problem.Charms.Where(c => positions.ContainsKey(c.InstanceId))
                .ToDictionary(c => positions[c.InstanceId], c => c));

        private static int Count(IReadOnlyDictionary<string, int> counts, string key) =>
            counts.TryGetValue(key, out var value) ? value : 0;

        private static Dictionary<string, int> Adjust(IReadOnlyDictionary<string, int>? reported,
            IReadOnlyDictionary<string, int> current, IReadOnlyDictionary<string, int> next)
        {
            var result = new Dictionary<string, int>();
            foreach (var key in current.Keys.Union(next.Keys).Union(reported?.Keys ?? Enumerable.Empty<string>()))
                result[key] = (reported is null ? Count(current, key) : Count(reported, key)) - Count(current, key) + Count(next, key);
            return result;
        }
    }
}
