using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// 석판 후보가 놓일 자리에서 실제로 미치는 효과. 증가분이 같아 보일 때 무엇이 다른지
        /// 알려주는 근거다. 아티팩트 후보는 비어 있다.
        /// </summary>
        public TabletEffectSummary Effect { get; set; } = new TabletEffectSummary();

        /// <summary>후보를 집었을 때의 콤보 진행. "잉걸불 7/8" 꼴. 콤보와 무관하면 빈 문자열.</summary>
        public string ComboText { get; set; } = "";

        /// <summary>이 후보로 콤보 임계값에 닿아 새 효과가 발동하는가.</summary>
        public bool ComboCompletes { get; set; }

        /// <summary>줄 세우기에 더해지는 콤보 가치. 점수 증가분과 같은 단위로 환산한 것이다.</summary>
        public double ComboBonus { get; set; }

        /// <summary>사용자가 밀고 있는 빌드 카테고리에 속하는 아티팩트인가.</summary>
        public bool MatchesPriority { get; set; }

        /// <summary>가져온 프리셋의 빌드가 즐겨찾기로 찍어 둔 아티팩트인가.</summary>
        public bool MatchesPreset { get; set; }

        /// <summary>가방이 차 있어 이 후보를 집으면 자리를 내줘야 하는 것의 이름. 없으면 빈 문자열.</summary>
        public string Displaced { get; set; } = "";

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
        /// <summary>후보마다 한 번씩 푸는 만큼, 기본 탐색보다 가볍게 잡는다.</summary>
        private static readonly SolverOptions Faster = new() { BeamWidth = 150, ExactCandidates = 40 };

        /// <summary>밀고 있는 카테고리의 콤보 가치를 몇 배로 칠지.</summary>
        private const double PriorityMultiplier = 3.0;

        /// <summary>콤보 진행과 무관하게, 밀고 있는 카테고리라는 것만으로 얹는 가치.</summary>
        private const double PriorityWorth = 0.5;

        /// <summary>
        /// 프리셋 빌드가 즐겨찾기로 찍은 아티팩트에 얹는 가치. 카테고리보다 구체적인 지목이라
        /// 조금 더 크게 잡았다. 다른 값들과 마찬가지로 실측 근거가 없는 설계값이다.
        /// </summary>
        private const double PresetCharmWorth = 1.0;

        public static List<OfferAdvice> Rank(
            PlacementProblem problem, IReadOnlyList<OfferCandidate> candidates, int gold,
            IReadOnlyDictionary<string, int>? comboCounts = null,
            Func<string, ComboDefinition?>? combos = null,
            IReadOnlyCollection<string>? priorityCategories = null,
            IReadOnlyCollection<int>? presetCharms = null,
            CharmValueBook? values = null)
        {
            // 기준과 후보를 같은 탐색 강도로 풀어야 증가분이 순수하게 후보의 몫이 된다. 기준만
            // 촘촘한 탐색으로 풀면, 명백히 좋은 후보에도 탐색 강도 차이만큼 음수가 나온다.
            var baseScore = PlacementSolver.Solve(problem, Faster).Score;

            var advice = new List<OfferAdvice>();
            var nextInstanceId = -1;

            foreach (var candidate in candidates)
            {
                var trial = Clone(problem);
                var candidateId = nextInstanceId--;

                if (candidate.Charm is not null)
                {
                    trial.Charms.Add(new CharmSlot
                    {
                        Definition = candidate.Charm,
                        InstanceId = candidateId,
                        IsDormant = candidate.CharmIsDormant,
                        Worth = CharmWorth.Resolve(candidate.Charm, values?.Of(candidate.Charm)),
                    });
                }
                else if (candidate.Tablet is not null)
                {
                    trial.Tablets.Add(new TabletSlot
                    {
                        Definition = candidate.Tablet,
                        InstanceId = candidateId,
                        Rotatable = candidate.Tablet.IsRotatable,
                    });
                }
                else
                {
                    continue;
                }

                var solved = PlacementSolver.Solve(trial, Faster);
                var entry = new OfferAdvice
                {
                    Candidate = candidate,
                    Gain = solved.Score - baseScore,
                    Affordable = candidate.Price <= gold,
                    Effect = EffectOf(candidate, trial, solved),
                    Displaced = DisplacedBy(trial, solved, candidateId),
                    Trial = trial,
                    Solved = solved,
                };
                EvaluateCombo(entry, comboCounts, combos, priorityCategories, presetCharms);
                advice.Add(entry);
            }

            // 살 수 없는 것은 아무리 좋아도 지금 고를 수 없다. 지우지는 않고 아래로 내린다.
            // 콤보 가치는 배치 점수에 안 잡히므로 여기서 더해 줄을 세운다. 증가분까지 같으면
            // 여력이 큰 쪽을 위로 올린다. 아티팩트가 적을 때는 여러 석판이 똑같이 최대치를
            // 뽑아내 증가분만으로는 우열이 드러나지 않는다. 그래도 같으면 정의 번호로 가른다 -
            // 입력 순서는 게임의 오브젝트 열거 순서라 폴링마다 흔들릴 수 있다.
            return advice.OrderByDescending(entry => entry.Affordable)
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
            IReadOnlyCollection<int>? presetCharms)
        {
            var charm = advice.Candidate.Charm;
            if (charm is null) return;

            if (presetCharms is not null && presetCharms.Contains(charm.EntityId))
            {
                advice.MatchesPreset = true;
                advice.ComboBonus += PresetCharmWorth;
            }

            foreach (var category in charm.Categories)
            {
                if (priorityCategories is null || !priorityCategories.Contains(category)) continue;

                advice.MatchesPriority = true;
                advice.ComboBonus += PriorityWorth;
                break;
            }
            if (comboCounts is null || combos is null) return;

            var parts = new List<string>();
            foreach (var category in charm.Categories)
            {
                var combo = combos(category);
                if (combo is null || combo.Thresholds.Count == 0) continue;

                var priority = priorityCategories is not null && priorityCategories.Contains(category);
                var worth = priority ? PriorityMultiplier : 1;

                comboCounts.TryGetValue(category, out var current);
                var step = Worth.OfComboStep(combo, current, out var completes, out var goal);
                if (step <= 0) continue; // 임계값을 다 넘겼다 - 더 모아도 변하는 것이 없다.

                if (completes) advice.ComboCompletes = true;
                advice.ComboBonus += step * worth;

                var name = Naming.Of(combo.Names, combo.Id, "?");
                parts.Add($"{name} {current + 1}/{goal}");
            }
            advice.ComboText = string.Join(" ", parts);
        }

        /// <summary>
        /// 후보 석판이 실제로 놓인 자리에서 세어야 뜻이 있다. 같은 석판도 어디에 어떻게 놓느냐에
        /// 따라 격자 밖으로 나가는 칸이 달라진다. 후보는 마지막에 넣었으므로 배치도 마지막이다.
        /// </summary>
        private static TabletEffectSummary EffectOf(
            OfferCandidate candidate, PlacementProblem trial, Arrangement solved)
        {
            if (candidate.Tablet is null) return new TabletEffectSummary();

            var index = trial.Tablets.Count - 1;
            if (index < 0 || index >= solved.Tablets.Count) return new TabletEffectSummary();

            var placement = solved.Tablets[index];
            return TabletEffectSummary.Of(placement.Query, trial.Grid, placement.Position, placement.Rotation);
        }

        /// <summary>
        /// 가방이 차 있으면 후보를 집는 값에 "무엇을 빼는가"가 이미 들어 있다(솔버가 배정에서
        /// 떨어뜨린다). 그 사실을 이름으로 드러낸다.
        /// </summary>
        private static string DisplacedBy(PlacementProblem trial, Arrangement solved, int candidateId)
        {
            foreach (var charm in trial.Charms)
            {
                if (charm.InstanceId == candidateId) continue;
                if (solved.CharmPositions.ContainsKey(charm.InstanceId)) continue;

                return Naming.Of(
                    charm.Definition.Names, charm.Definition.Id, charm.IsFiller ? "아이템" : "아티팩트");
            }
            return "";
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
            };

            // 현재 위치를 빼먹으면 후보 쪽 풀이만 현 배치 후보와 안정 보너스를 잃어,
            // 기준과 후보가 서로 다른 조건으로 풀리게 된다.
            foreach (var pair in problem.CurrentTablets) clone.CurrentTablets[pair.Key] = pair.Value;
            foreach (var pair in problem.CurrentCharms) clone.CurrentCharms[pair.Key] = pair.Value;
            return clone;
        }
    }
}
