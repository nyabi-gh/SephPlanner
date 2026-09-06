using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    public sealed class OfferCandidate
    {
        public int DefinitionId { get; set; }
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public int Price { get; set; }
        public CharmDefinition? Charm { get; set; }
        public TabletDefinition? Tablet { get; set; }

        /// <summary>무기 연동인데 지금 든 무기와 맞지 않는 아티팩트. 집어도 효과가 없다.</summary>
        public bool CharmIsDormant { get; set; }
    }

    public sealed class OfferAdvice
    {
        public OfferCandidate Candidate { get; set; } = new OfferCandidate();
        public double Gain { get; set; }

        /// <summary>지금 소지금으로 살 수 있는지. 그냥 집으면 되는 것은 항상 참이다.</summary>
        public bool Affordable { get; set; }

        /// <summary>게임의 한 칸 인벤토리 규칙으로 후보를 반드시 포함할 수 있는가.</summary>
        public bool Available { get; set; } = true;

        public bool CandidatePlaced { get; set; }

        /// <summary>
        /// 석판 후보가 놓일 자리에서 실제로 미치는 효과. 증가분이 같아 보일 때 무엇이 다른지
        /// 알려주는 근거다. 아티팩트 후보는 비어 있다.
        /// </summary>
        public TabletEffectSummary Effect { get; set; } = new TabletEffectSummary();

        /// <summary>후보를 집었을 때의 콤보 진행. "잉걸불 7/8" 꼴. 콤보와 무관하면 빈 문자열.</summary>
        public string ComboText { get; set; } = "";

        /// <summary>이 후보로 콤보 임계값에 닿아 새 효과가 발동하는가.</summary>
        public bool ComboCompletes { get; set; }

        /// <summary>교체 때문에 이미 발동한 콤보 임계값 아래로 내려가는가.</summary>
        public bool ComboLoses { get; set; }

        /// <summary>줄 세우기에 더해지는 콤보 가치. 점수 증가분과 같은 단위로 환산한 것이다.</summary>
        public double ComboBonus { get; set; }

        /// <summary>사용자가 밀고 있는 빌드 카테고리에 속하는 아티팩트인가.</summary>
        public bool MatchesPriority { get; set; }

        /// <summary>가져온 프리셋의 빌드가 즐겨찾기로 찍어 둔 아티팩트인가.</summary>
        public bool MatchesPreset { get; set; }

        /// <summary>가방이 차 있어 이 후보를 집으면 자리를 내줘야 하는 것의 이름. 없으면 빈 문자열.</summary>
        public string Displaced { get; set; } = "";
        public OfferDisplacement? Displacement { get; set; }

        /// <summary>
        /// 이 후보를 집었을 때의 격자. 증가분이라는 숫자 하나로는 무엇이 어떻게 달라지는지
        /// 알 수 없어, 이미 푼 결과를 버리지 않고 들고 있다가 화면에 그대로 보여준다.
        /// </summary>
        public PlanPreview? Preview { get; set; }

        /// <summary>미리보기를 만들 재료. 채우는 것은 <c>PlanBuilder</c> 몫이다.</summary>
        internal PlacementProblem? Trial { get; set; }
        internal Arrangement? Solved { get; set; }

        /// <summary>
        /// 이 후보를 가리키는 열쇠. 계획은 스냅샷마다 새로 풀리므로 객체로는 같은 후보를 다시
        /// 알아볼 수 없는데, 미리보기는 고른 것을 판이 바뀌어도 붙들고 있어야 한다. 종류가 같은
        /// 후보는 하나로 묶여 오지만 합성 석판처럼 엔티티가 같고 이름이 다른 것이 있어 이름까지 넣는다.
        /// </summary>
        public string Key => $"{Candidate.Kind}:{Candidate.DefinitionId}:{Candidate.Name}";
    }

    public sealed class OfferDisplacement
    {
        public int InstanceId { get; set; }
        public int DefinitionId { get; set; }
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
    }

    /// <summary>후보를 집었다고 쳤을 때의 배치. 화면이 그리는 데 필요한 것만 담는다.</summary>
    public sealed class PlanPreview
    {
        public List<TabletPlacement> Tablets { get; set; } = new List<TabletPlacement>();
        public Dictionary<GridPos, string> Names { get; set; } = new Dictionary<GridPos, string>();
        public Dictionary<GridPos, int> Charms { get; set; } = new Dictionary<GridPos, int>();
        public Dictionary<GridPos, int> Levels { get; set; } = new Dictionary<GridPos, int>();
        public Dictionary<GridPos, int> EffectiveLevels { get; set; } = new Dictionary<GridPos, int>();
        public Dictionary<GridPos, CharmInactiveReason> InactiveCells { get; set; } =
            new Dictionary<GridPos, CharmInactiveReason>();

        /// <summary>지금 배치와 달라지는 칸. 무엇이 바뀌는지가 미리보기의 요점이다.</summary>
        public HashSet<GridPos> Changed { get; set; } = new HashSet<GridPos>();

        public double Score { get; set; }
    }

    /// <summary>
    /// 지금 집을 수 있는 후보들을 점수 증가분으로 줄 세운다.
    ///
    /// 후보를 넣은 채로 최적 배치를 다시 풀기 때문에, 가방이 꽉 찼을 때 무엇을 밀어내야 하는지와
    /// 새 석판에 맞춰 기존 배치를 어떻게 바꿔야 하는지가 증가분에 이미 반영된다.
    /// </summary>
    public static class OfferAdvisor
    {
        private sealed class TrialOutcome
        {
            public PlacementProblem Trial = new PlacementProblem();
            public Arrangement Solved = new Arrangement();
            public OfferDisplacement? Displacement;
            public CharmSlot? DisplacedCharm;

            /// <summary>
            /// 이 갈래가 빼낸 석판이 원래 목록에서 몇 번째였는지. 석판을 그대로 둔 갈래는 -1 이다.
            /// 석판을 하나 뺀 갈래의 배치는 그대로 둔 갈래의 배치에서 그 자리만 빼면 나오므로,
            /// 이 번호가 있으면 탐색을 다시 돌리지 않아도 된다.
            /// </summary>
            public int RemovedTabletIndex = -1;
        }

        /// <summary>밀고 있는 카테고리의 콤보 가치를 몇 배로 칠지.</summary>
        private const double PriorityMultiplier = 3.0;

        /// <summary>콤보 진행과 무관하게, 밀고 있는 카테고리라는 것만으로 얹는 가치.</summary>
        private const double PriorityWorth = 0.5;


        public static List<OfferAdvice> Rank(
            PlacementProblem problem, IReadOnlyList<OfferCandidate> candidates, int gold,
            IReadOnlyDictionary<string, int>? comboCounts = null,
            Func<string, ComboDefinition?>? combos = null,
            IReadOnlyCollection<string>? priorityCategories = null,
            IReadOnlyCollection<int>? presetCharms = null,
            CharmValueBook? values = null,
            LayoutCache? layouts = null,
            CancellationToken cancellation = default)
        {
            // 볼 것이 없으면 기준 점수조차 풀 이유가 없다. 상자도 상점도 열지 않은 평상시가
            // 이 경우이고, 그때가 프레임을 가장 아껴야 할 때다.
            if (candidates.Count == 0) return new List<OfferAdvice>();

            layouts ??= new LayoutCache();
            var faster = SolverOptions.ForAdvice(cancellation);

            // 기준과 후보를 같은 탐색 강도로, 그리고 같은 배치 후보들 위에서 풀어야 증가분이
            // 순수하게 후보의 몫이 된다. 기준만 촘촘한 탐색으로 풀면, 명백히 좋은 후보에도 탐색
            // 강도 차이만큼 음수가 나온다.
            var baseScore = layouts.Baseline(problem, faster).Score;

            var advice = new List<OfferAdvice>();
            var nextInstanceId = -1;

            foreach (var candidate in candidates)
            {
                // 버릴 것이 정해진 풀이다. 남은 후보를 마저 보는 것은 그대로 낭비다.
                if (cancellation.IsCancellationRequested) break;

                var candidateId = nextInstanceId--;
                var outcome = BestTrial(problem, candidate, candidateId, values, layouts, faster);
                var entry = new OfferAdvice
                {
                    Candidate = candidate,
                    Affordable = candidate.Price <= gold,
                    Available = outcome is not null,
                    CandidatePlaced = outcome is not null,
                };
                if (outcome is not null)
                {
                    entry.Gain = outcome.Solved.Score - baseScore;
                    entry.Effect = EffectOf(candidate, outcome.Trial, outcome.Solved, candidateId);
                    entry.Displacement = outcome.Displacement;
                    entry.Displaced = outcome.Displacement?.Name ?? "";
                    entry.Trial = outcome.Trial;
                    entry.Solved = outcome.Solved;
                }
                EvaluateCombo(
                    entry, comboCounts, combos, priorityCategories, presetCharms,
                    outcome?.DisplacedCharm);
                advice.Add(entry);
            }

            // 살 수 없는 것은 아무리 좋아도 지금 고를 수 없다. 지우지는 않고 아래로 내린다.
            // 그다음은 가져온 빌드가 지목한 아티팩트다 - 빌드의 축이라 점수와 상관없이 먼저 권한다.
            // 콤보 가치는 배치 점수에 안 잡히므로 여기서 더해 줄을 세운다. 증가분까지 같으면
            // 여력이 큰 쪽을 위로 올린다. 아티팩트가 적을 때는 여러 석판이 똑같이 최대치를
            // 뽑아내 증가분만으로는 우열이 드러나지 않는다. 그래도 같으면 정의 번호로 가른다 -
            // 입력 순서는 게임의 오브젝트 열거 순서라 폴링마다 흔들릴 수 있다.
            return advice.OrderByDescending(entry => entry.Available)
                         .ThenByDescending(entry => entry.Affordable)
                         .ThenByDescending(entry => entry.MatchesPreset)
                         .ThenByDescending(entry => entry.Gain + entry.ComboBonus)
                         .ThenByDescending(entry => entry.Effect.Reach)
                         .ThenBy(entry => entry.Candidate.DefinitionId)
                         .ToList();
        }

        /// <summary>
        /// 후보를 집으면 콤보가 얼마나 나아가는지. 개수는 배치와 무관하게 격자의 아티팩트 전체로
        /// 세므로(게임 <c>SearchSetEffectInInventory</c>) 배치 솔버 밖에서 따로 평가한다.
        /// 현재 개수는 게임이 세어 둔 값(스냅샷의 <c>ComboCounts</c>)을 그대로 쓴다.
        /// </summary>
        private static void EvaluateCombo(
            OfferAdvice advice,
            IReadOnlyDictionary<string, int>? comboCounts,
            Func<string, ComboDefinition?>? combos,
            IReadOnlyCollection<string>? priorityCategories,
            IReadOnlyCollection<int>? presetCharms,
            CharmSlot? displacedCharm)
        {
            var charm = advice.Candidate.Charm;
            if (charm is null) return;

            // 가산점이 아니라 줄 세우기의 한 단계다(Rank). 가져온 빌드가 지목한 아티팩트는 뜨기만
            // 하면 맨 위로 온다 - 점수로 겨루게 두면 배치 이득이 큰 남에게 밀려 가져온 뜻이 없다.
            if (presetCharms is not null && presetCharms.Contains(charm.EntityId)) advice.MatchesPreset = true;

            foreach (var category in charm.Categories)
            {
                if (priorityCategories is null || !priorityCategories.Contains(category)) continue;

                advice.MatchesPriority = true;
                advice.ComboBonus += PriorityWorth;
                break;
            }
            if (!advice.Available || comboCounts is null || combos is null) return;

            var parts = new List<string>();
            var deltas = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var category in charm.Categories.Distinct(StringComparer.Ordinal))
                deltas[category] = 1;
            if (displacedCharm is not null && !displacedCharm.IsFiller)
            {
                foreach (var category in displacedCharm.Definition.Categories.Distinct(StringComparer.Ordinal))
                {
                    deltas.TryGetValue(category, out var delta);
                    deltas[category] = delta - 1;
                }
            }

            foreach (var pair in deltas.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var category = pair.Key;
                if (pair.Value == 0) continue;
                var combo = combos(category);
                if (combo is null || combo.Thresholds.Count == 0) continue;

                var priority = priorityCategories is not null && priorityCategories.Contains(category);
                var worth = priority ? PriorityMultiplier : 1;

                comboCounts.TryGetValue(category, out var current);
                var next = Math.Max(0, current + pair.Value);
                var change = ComboValue(combo, next) - ComboValue(combo, current);
                if (Math.Abs(change) < 0.000001) continue;

                if (CrossesUp(combo, current, next)) advice.ComboCompletes = true;
                if (CrossesDown(combo, current, next)) advice.ComboLoses = true;
                advice.ComboBonus += change * worth;

                var name = Naming.Of(combo.Names, combo.Id, "?");
                parts.Add($"{name} {next}/{Goal(combo, next, pair.Value)}");
            }
            advice.ComboText = string.Join(" ", parts);
        }

        private static double ComboValue(ComboDefinition combo, int count)
        {
            var total = 0.0;
            var cap = combo.Thresholds.Count == 0 ? 0 : combo.Thresholds.Max();
            for (var current = 0; current < Math.Min(count, cap); current++)
                total += Worth.OfComboStep(combo, current, out _, out _);
            return total;
        }

        private static bool CrossesUp(ComboDefinition combo, int current, int next) =>
            combo.Thresholds.Any(threshold => current < threshold && next >= threshold);

        private static bool CrossesDown(ComboDefinition combo, int current, int next) =>
            combo.Thresholds.Any(threshold => current >= threshold && next < threshold);

        private static int Goal(ComboDefinition combo, int count, int delta)
        {
            var ordered = combo.Thresholds.OrderBy(value => value).ToList();
            if (delta > 0)
            {
                foreach (var threshold in ordered)
                    if (threshold >= count) return threshold;
            }
            else
            {
                foreach (var threshold in ordered)
                    if (threshold > count) return threshold;
            }
            return ordered[ordered.Count - 1];
        }

        /// <summary>
        /// 후보 석판이 실제로 놓인 자리에서 세어야 뜻이 있다. 같은 석판도 어디에 어떻게 놓느냐에
        /// 따라 격자 밖으로 나가는 칸이 달라진다. 후보는 마지막에 넣었으므로 배치도 마지막이다.
        /// </summary>
        private static TabletEffectSummary EffectOf(
            OfferCandidate candidate, PlacementProblem trial, Arrangement solved, int candidateId)
        {
            if (candidate.Tablet is null) return new TabletEffectSummary();
            if (!solved.TabletPositions.TryGetValue(candidateId, out var spot))
                return new TabletEffectSummary();

            var placement = trial.Tablets.First(slot => slot.InstanceId == candidateId)
                .At(spot.Position, spot.Rotation);
            return TabletEffectSummary.Of(placement.Query, trial.Grid, placement.Position, placement.Rotation);
        }

        /// <summary>
        /// 이 후보를 집는 여러 갈래 중 가장 좋은 것. 가방이 차 있으면 무엇을 밀어내느냐로 갈래가
        /// 갈리는데, <b>아티팩트를 밀어내는 갈래들은 석판 구성이 모두 같다</b>. 그래서 배치 탐색은
        /// <see cref="LayoutCache"/>가 한 번만 돌리고, 갈래마다는 정확한 배정만 다시 푼다.
        /// </summary>
        private static TrialOutcome? BestTrial(
            PlacementProblem problem, OfferCandidate candidate, int candidateId, CharmValueBook? values,
            LayoutCache layouts, SolverOptions faster)
        {
            TrialOutcome? best = null;

            // 석판을 그대로 둔 갈래들의 배치. 석판을 하나 빼는 갈래는 여기서 그 자리만 빼면
            // 되므로, 그런 갈래마다 탐색을 다시 돌리지 않는다 - 그 탐색들이 남은 비용의 대부분이었다.
            IReadOnlyList<List<TabletPlacement>>? kept = null;

            foreach (var outcome in Trials(problem, candidate, candidateId, values))
            {
                IReadOnlyList<List<TabletPlacement>> yardstick;
                if (outcome.RemovedTabletIndex < 0)
                {
                    yardstick = layouts.Yardstick(outcome.Trial, faster);
                    kept = yardstick;
                }
                else
                {
                    yardstick = Without(kept, outcome.RemovedTabletIndex)
                                ?? layouts.Yardstick(outcome.Trial, faster);
                }

                outcome.Solved = PlacementSolver.EvaluateLayouts(outcome.Trial, yardstick, faster);
                var placed = candidate.Charm is not null
                    ? outcome.Solved.CharmPositions.ContainsKey(candidateId)
                    : outcome.Solved.TabletPositions.ContainsKey(candidateId);
                if (!placed) continue;
                if (best is null || PriorityComboPlacement.Compare(outcome.Solved, best.Solved) > 0) best = outcome;
            }
            if (best is null) return null;

            // 이긴 갈래만 배치 후보 전부로 다시 푼다. 화면에 나가는 증가분은 기준 점수와 같은
            // 잣대에서 나와야 하고, 미리보기도 이 결과를 그대로 쓴다.
            best.Solved = PlacementSolver.EvaluateLayouts(
                best.Trial, layouts.Of(best.Trial, faster), faster);
            return best;
        }

        private static IEnumerable<TrialOutcome> Trials(
            PlacementProblem problem, OfferCandidate candidate, int candidateId, CharmValueBook? values)
        {
            var occupied = problem.Charms.Count + problem.Tablets.Count;
            if (occupied < problem.Grid.Storage)
            {
                var trial = Clone(problem);
                if (AddCandidate(trial, candidate, candidateId, values))
                    yield return new TrialOutcome { Trial = trial };
                yield break;
            }

            foreach (var charm in problem.Charms.OrderBy(value => value.InstanceId))
            {
                var trial = Clone(problem);
                trial.Charms.RemoveAll(value => value.InstanceId == charm.InstanceId);
                trial.CurrentCharms.Remove(charm.InstanceId);
                trial.PlannedCharms.Remove(charm.InstanceId);
                if (trial.Charms.Count + trial.Tablets.Count >= problem.Grid.Storage ||
                    !AddCandidate(trial, candidate, candidateId, values))
                    continue;

                var kind = charm.IsFiller ? "filler" : "charm";
                yield return new TrialOutcome
                {
                    Trial = trial,
                    DisplacedCharm = charm,
                    Displacement = new OfferDisplacement
                    {
                        InstanceId = charm.InstanceId,
                        DefinitionId = charm.Definition.EntityId,
                        Kind = kind,
                        Name = Naming.Of(
                            charm.Definition.Names, charm.Definition.Id,
                            charm.IsFiller ? "아이템" : "아티팩트"),
                    },
                };
            }

            foreach (var tablet in problem.Tablets.OrderBy(value => value.InstanceId))
            {
                var removed = problem.Tablets.FindIndex(value => value.InstanceId == tablet.InstanceId);
                var trial = Clone(problem);
                trial.Tablets.RemoveAll(value => value.InstanceId == tablet.InstanceId);
                trial.CurrentTablets.Remove(tablet.InstanceId);
                trial.PlannedTablets.Remove(tablet.InstanceId);
                if (trial.Charms.Count + trial.Tablets.Count >= problem.Grid.Storage ||
                    !AddCandidate(trial, candidate, candidateId, values))
                    continue;

                yield return new TrialOutcome
                {
                    Trial = trial,
                    RemovedTabletIndex = removed,
                    Displacement = new OfferDisplacement
                    {
                        InstanceId = tablet.InstanceId,
                        DefinitionId = tablet.Definition.EntityId,
                        Kind = "tablet",
                        Name = Naming.Of(tablet.Definition.Names, tablet.Definition.Id, "석판"),
                    },
                };
            }
        }

        /// <summary>
        /// 석판 하나를 뺀 배치.
        ///
        /// 배치 후보는 어느 경로로 만들어지든 <c>problem.Tablets</c> 순서를 지킨다 - 빔은 앞에서부터
        /// 한 장씩 붙이고, 현재 배치와 직전 제안은 그 순서로 짓거나 아예 만들지 않는다. 그래서
        /// 석판이 다 놓이지 못한 배치에서도 <c>layout[i]</c>는 <c>Tablets[i]</c>이고, 그 번호의
        /// 자리만 빼면 짝이 맞는다. 만들지 못하는 것은 배치 길이보다 뒤를 빼라고 할 때뿐이다.
        /// </summary>
        private static IReadOnlyList<List<TabletPlacement>>? Without(
            IReadOnlyList<List<TabletPlacement>>? layouts, int index)
        {
            if (layouts is null || layouts.Count == 0) return null;

            var layout = layouts[0];
            if (index < 0 || index >= layout.Count) return null;

            var reduced = new List<TabletPlacement>(layout);
            reduced.RemoveAt(index);
            return new List<List<TabletPlacement>> { reduced };
        }

        private static bool AddCandidate(
            PlacementProblem trial, OfferCandidate candidate, int candidateId, CharmValueBook? values)
        {
            if (candidate.Charm is not null)
            {
                trial.Charms.Add(new CharmSlot
                {
                    Definition = candidate.Charm,
                    InstanceId = candidateId,
                    IsDormant = candidate.CharmIsDormant,
                    Worth = CharmWorth.Resolve(candidate.Charm, values?.Of(candidate.Charm)),
                });
                return true;
            }
            if (candidate.Tablet is null) return false;

            trial.Tablets.Add(new TabletSlot
            {
                Definition = candidate.Tablet,
                InstanceId = candidateId,
                Rotatable = candidate.Tablet.IsRotatable,
            });
            return true;
        }

        private static PlacementProblem Clone(PlacementProblem problem)
        {
            var clone = new PlacementProblem
            {
                Grid = problem.Grid,
                Charms = new List<CharmSlot>(problem.Charms),
                Tablets = new List<TabletSlot>(problem.Tablets),
                FixedTablets = problem.FixedTablets,
                FixedEffects = problem.FixedEffects,
                ComboCounts = problem.ComboCounts,
                Combos = problem.Combos,
                PriorityCategories = problem.PriorityCategories,
            };

            // 현재 위치와 직전 제안을 빼먹으면 후보 쪽 풀이만 앵커를 잃어, 기준과 후보가
            // 서로 다른 조건으로 풀리게 된다.
            foreach (var pair in problem.CurrentTablets) clone.CurrentTablets[pair.Key] = pair.Value;
            foreach (var pair in problem.CurrentCharms) clone.CurrentCharms[pair.Key] = pair.Value;
            foreach (var pair in problem.PlannedTablets) clone.PlannedTablets[pair.Key] = pair.Value;
            foreach (var pair in problem.PlannedCharms) clone.PlannedCharms[pair.Key] = pair.Value;
            return clone;
        }
    }
}
