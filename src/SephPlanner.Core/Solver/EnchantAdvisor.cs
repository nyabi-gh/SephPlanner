using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    public sealed class EnchantAdvice
    {
        public int InstanceId { get; set; }
        public string Name { get; set; } = "";

        /// <summary>
        /// 지금 앉아 있는 칸. 제단이 여는 창이 가방을 그대로 보여주므로 사람은 이 자리를 누르면
        /// 된다. 인챈트를 걸면 배치가 달라질 수 있지만 그것은 누른 뒤의 이야기다.
        /// </summary>
        public GridPos Position { get; set; }

        public double Gain { get; set; }

        /// <summary>
        /// 다음 칸이 열렸다고 쳤을 때의 증가분. 제단은 사람마다 정해진 횟수뿐이라 되돌릴 수
        /// 없고, 지금 가방에서만 값이 낮은 아티팩트가 밀리면 그 손해도 되돌릴 수 없다
        /// (<see cref="Lookahead"/>). 앞을 보지 못했으면 <c>null</c> 이다.
        /// </summary>
        public double? SoonGain { get; set; }

        /// <summary>줄 세우기에 실제로 쓰이는 증가분.</summary>
        public double RankedGain => SoonGain ?? Gain;

        /// <summary>걸고 난 뒤의 인챈트.</summary>
        public int Enchant { get; set; }

        /// <summary>
        /// 인챈트가 닿을 수 있는 상한. 게임은 인챈트 수치가 이 값에 닿으면 더 받지 않는다
        /// (<c>UI_CharacterStatusPanel</c>). <b>석판이 준 레벨과는 무관하다</b> - 상한이 걸리는
        /// 것은 인챈트 수치 자체다.
        /// </summary>
        public int MaxEnchant { get; set; }

        /// <summary>이 인챈트로 효과가 되살아나는 아티팩트. 음수 레벨이 0 이상이 되는 자리다.</summary>
        public List<string> Activated { get; set; } = new List<string>();
    }

    /// <summary>
    /// 인챈트 제단에서 어느 아티팩트에 걸어야 하는지. 가상 배치만 비교하며 인챈트 명령을 만들지는
    /// 않는다 - 게임이 확인 창을 띄우고 사람이 누른다.
    ///
    /// <b>제일 센 아티팩트가 답이 아니다.</b> 효과에 반영되는 레벨은 <c>min(maxLevel, 표시 레벨)</c>
    /// 이라 이미 상한 위에 앉은 아티팩트는 인챈트를 걸어도 값이 0 이고, 반대로 배수 칸에서는
    /// 인챈트 +1 이 레벨 +배수가 된다(인챈트는 배수보다 먼저 더해진다 - RESEARCH 의 "레벨이
    /// 정해지는 순서"). 그래서 답은 대개 "상한 아래에 있으면서 배수 칸에 앉은 것"이고, 그것은
    /// 눈으로 세기 어려운 종류다.
    ///
    /// <b>싸지만 공짜는 아니다.</b> 인챈트는 아무것도 움직이지 않으므로 석판 자리를 다시 찾을
    /// 일이 없어 빔 탐색은 둘(기준과 늘어난 판)로 끝난다. 그래도 후보마다 배정을 다시 푸는 값은
    /// 남는다 - 아티팩트 35개 가방에서 재어 보니 <b>377ms</b> 로, 하나 빼기(272ms)보다 비싸고
    /// 합성(838ms)·후보 8개(1,108ms)보다 싸다. 그래서 부르는 쪽이 합성기처럼 거리로 막는다
    /// (<c>EnchantChanceState.Near</c>).
    /// </summary>
    public static class EnchantAdvisor
    {
        /// <param name="baseline">아무것도 걸지 않은 기준 배치. 증가분은 이것과 견준다.</param>
        /// <param name="layouts">
        /// 배치 탐색을 나눠 쓸 자리. 인챈트는 석판 구성을 바꾸지 않으므로 기준 배치의 자리와
        /// 빔 몇 개를 그대로 쓰고, 후보마다 다시 푸는 것은 아티팩트 배정뿐이다.
        /// </param>
        /// <param name="lookahead">
        /// 다음 칸이 열린 판. 지금 가방에서 이득이 없다는 이유로 밀린 아티팩트가 칸 하나로
        /// 답이 되는 경우를 잡는다.
        /// </param>
        public static List<EnchantAdvice> Rank(
            PlacementProblem problem, Arrangement baseline,
            LayoutCache? layouts = null, Lookahead? lookahead = null,
            CancellationToken cancellation = default)
        {
            var advice = new List<EnchantAdvice>();
            if (problem.Charms.Count == 0 || baseline.UnplacedTablets > 0) return advice;
            cancellation.ThrowIfCancellationRequested();

            layouts ??= new LayoutCache();
            var options = SolverOptions.ForAdvice(cancellation);
            options.EmptySideTrials = 12;
            options.PriorityComboTrials = 24;

            // 후보마다 빔을 다시 만들지 않는다. 석판은 그대로이므로 현재·제안 배치를 포함한
            // 공통 후보 위에서 아티팩트 배정만 다시 본다.
            var candidateLayouts = new List<List<TabletPlacement>> { baseline.Tablets };
            candidateLayouts.AddRange(layouts.Of(problem, options).Take(4));

            var soonBaseline = lookahead?.Baseline(layouts, options);
            var soonLayouts = new List<List<TabletPlacement>>();
            if (soonBaseline is not null)
            {
                soonLayouts.Add(soonBaseline.Tablets);
                soonLayouts.AddRange(candidateLayouts);
            }

            foreach (var charm in problem.Charms.OrderBy(c => c.InstanceId))
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Enchantable(charm)) continue;

                var trial = OfferAdvisor.Clone(problem);
                var index = trial.Charms.FindIndex(c => c.InstanceId == charm.InstanceId);
                if (index < 0) continue;

                // 슬롯을 제자리에서 고치면 기준 배치까지 함께 바뀐다 - Clone 은 목록만 얕게
                // 복사한다. 그래서 인챈트만 다른 사본으로 갈아 끼운다.
                trial.Charms[index] = charm.WithEnchant(charm.Enchant + 1);

                var solved = PlacementSolver.EvaluateLayouts(trial, candidateLayouts, options);
                cancellation.ThrowIfCancellationRequested();
                if (!Improves(solved, baseline, trial)) continue;

                // 여기까지 온 것만 늘어난 판에서 다시 본다.
                double? soonGain = null;
                if (soonBaseline is not null && lookahead is not null)
                {
                    var soon = lookahead.Solve(trial, layouts, options, soonLayouts);
                    if (soon is null || !Improves(soon, soonBaseline, trial)) continue;
                    soonGain = soon.Score - soonBaseline.Score;
                }

                advice.Add(new EnchantAdvice
                {
                    InstanceId = charm.InstanceId,
                    Name = Naming.Of(charm.Definition.Names, charm.Definition.Id, "아티팩트"),
                    Position = problem.CurrentCharms.TryGetValue(charm.InstanceId, out var cell) ? cell
                        : baseline.CharmPositions.TryGetValue(charm.InstanceId, out var placed) ? placed
                        : default,
                    Gain = solved.Score - baseline.Score,
                    SoonGain = soonGain,
                    Enchant = charm.Enchant + 1,
                    MaxEnchant = charm.Definition.MaxLevel,
                    Activated = trial.Charms
                        .Where(c => baseline.InactiveCharms.Contains(c.InstanceId) &&
                                    !solved.InactiveCharms.Contains(c.InstanceId))
                        .Select(c => Naming.Of(c.Definition.Names, c.Definition.Id, "아티팩트"))
                        .ToList(),
                });
            }

            return advice.OrderByDescending(a => a.RankedGain).ThenBy(a => a.InstanceId).Take(3).ToList();
        }

        /// <summary>
        /// 게임이 실제로 걸어 주는 것만 후보로 둔다. <c>UI_CharacterStatusPanel</c> 은 아티팩트가
        /// 아니거나, <c>maxLevel</c> 이 0 이거나, 인챈트가 이미 상한이면 메시지만 띄우고 돌려보낸다.
        /// </summary>
        private static bool Enchantable(CharmSlot charm)
        {
            if (charm.IsFiller) return false;
            if (charm.Definition.MaxLevel <= 0) return false;
            if (charm.Enchant >= charm.Definition.MaxLevel) return false;

            // 연동 무기를 안 든 아티팩트는 지금 효과가 없어 증가분을 잴 수가 없다. 무기를 바꿀
            // 생각으로 미리 걸어 두는 것은 미래를 재는 일이라 하지 않는다(ROADMAP 5번의
            // "배치 목적함수의 미래 항").
            return !charm.IsDormant;
        }

        /// <summary>
        /// 버리기 조언과 같은 잣대다. <see cref="PriorityPlacement.Compare"/> 를 함께 보는 것이
        /// 이 조언에서는 특히 중요하다 - 그쪽이 점수를 카탈로그의 눈금으로 끊고 초과 강화를 세므로,
        /// <b>상한 위로 넘쳐 낭비만 되는 인챈트</b>가 여기서 걸러진다.
        /// </summary>
        private static bool Improves(Arrangement solved, Arrangement baseline, PlacementProblem trial) =>
            solved.UnretainedCharms.Count == 0 &&
            solved.UnplacedTablets == 0 &&
            solved.CharmPositions.Count == trial.Charms.Count &&
            ActivationPolicy.AllowsTransition(baseline, solved) &&
            solved.Score > baseline.Score + 0.001 &&
            PriorityPlacement.Compare(solved, baseline) > 0;
    }
}
