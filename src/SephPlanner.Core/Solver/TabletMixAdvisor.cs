using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    public sealed class MixAdvice
    {
        public int InstanceA { get; set; }
        public int InstanceB { get; set; }
        public string NameA { get; set; } = "";
        public string NameB { get; set; } = "";

        /// <summary>재료를 합성기에 넣기 전에 맞춰 두어야 하는 회전. 사람이 따라 해야 한다.</summary>
        public int RotationA { get; set; }
        public int RotationB { get; set; }

        /// <summary>합성했을 때의 점수 증가분. 재료 둘이 사라지고 결과 하나가 생기는 것까지 반영된다.</summary>
        public double Gain { get; set; }

        public bool Affordable { get; set; }

        /// <summary>결과 석판이 실제로 미치는 범위. 증가분이 비슷할 때 무엇이 다른지 드러낸다.</summary>
        public TabletEffectSummary Effect { get; set; } = new TabletEffectSummary();

        public bool Rotatable { get; set; }

        /// <summary>합쳐진 질의. 결과가 무엇인지 툴팁에서 보이는 데 쓴다.</summary>
        public string Query { get; set; } = "";

        /// <summary>이 합성으로 만들어진 판. 줄 세운 뒤 상위 몇만 제대로 다시 푸는 데 쓴다.</summary>
        internal PlacementProblem? Trial { get; set; }
    }

    /// <summary>
    /// 가진 석판 중 어느 둘을 합치면 좋은지. 합성기는 선택지로 잡히지 않아 그동안 답이 없던 자리다.
    ///
    /// 후보 추천과 같은 방식이다 - 합친 상태로 배치를 다시 풀어 점수 증가분을 본다. 그래서
    /// 재료 둘이 없어지고 칸이 하나 비는 것과, 합쳐진 질의가 더 좋은 자리를 만드는 것이 증가분
    /// 안에 함께 들어간다.
    ///
    /// 탐색 범위는 작다. 쌍이 n(n-1)/2 이고 회전 조합은 넷을 넘지 않는다.
    /// </summary>
    public static class TabletMixAdvisor
    {
        /// <summary>
        /// 쌍마다 배치를 처음부터 다시 푸는 대신, <b>먼저 짐작으로 줄을 세우고 상위 몇만 제대로
        /// 푼다.</b> 짐작은 지금 배치에서 재료 둘을 빼고 그 빈자리에 결과를 놓아 보는 것이다 -
        /// 사람이 실제로 하는 일이기도 하고, 탐색 없이 채점만 하면 되어 백 배 싸다.
        ///
        /// <b>보장.</b> 돌려주는 것은 전부 다시 푼 값이므로, 보여주는 쌍의 증가분이 틀리는 일은
        /// 없다. 짐작이 잃을 수 있는 것은 <b>어느 쌍이 목록에 드느냐</b>다 - 좋은 쌍이 짐작에서
        /// 낮게 잡혀 잘려 나갈 수 있다. 그래서 <paramref name="limit"/>은 화면이 보여줄 줄 수보다
        /// 넉넉히 받는다(화면은 3줄, 기본값은 5).
        ///
        /// 실측 비교(석판 6, 42칸): 상위 3개는 쌍마다 제대로 푼 결과와 완전히 같았고, 넷째
        /// 자리에서 +2.70 인 쌍이 +0.65 인 쌍에 밀렸다. 대신 4.3초/6.1GB 가 0.54초/0.79GB 가 됐다.
        /// </summary>
        public static List<MixAdvice> Rank(
            PlacementProblem problem, ICatalog catalog, int cost, int gold, int limit = 5,
            LayoutCache? layouts = null, CancellationToken cancellation = default)
        {
            var materials = Materials(problem);
            if (materials.Count < 2) return new List<MixAdvice>();

            layouts ??= new LayoutCache();
            var faster = SolverOptions.ForAdvice(cancellation);
            var baseArrangement = layouts.Baseline(problem, faster);
            var baseScore = baseArrangement.Score;

            // 짐작의 바탕. 석판이 다 놓이지 못한 배치는 자리와 석판의 짝이 어긋나므로 쓰지 않고,
            // 그때는 쌍마다 제대로 푼다(그런 판은 석판이 칸보다 많은 극단이라 쌍도 몇 개 안 된다).
            var baseLayout = baseArrangement.UnplacedTablets == 0 &&
                             baseArrangement.Tablets.Count == problem.Tablets.Count
                ? baseArrangement.Tablets
                : null;

            var mixedDefinition = TabletMix.Definition(catalog.Tablet(TabletMix.ResultEntityId));

            var advice = new List<MixAdvice>();
            for (var i = 0; i < materials.Count && !cancellation.IsCancellationRequested; i++)
            {
                for (var j = i + 1; j < materials.Count; j++)
                {
                    var best = Best(
                        problem, materials[i], materials[j], mixedDefinition, baseScore,
                        layouts, baseLayout, i, j, faster);
                    if (best is not null) advice.Add(best);
                }
            }

            foreach (var entry in advice) entry.Affordable = cost <= gold;

            // 증가분이 같은 쌍이 여럿일 때 인스턴스 번호로 갈라야 제안이 흔들리지 않는다.
            var shown = advice
                .OrderByDescending(entry => entry.Gain)
                .ThenBy(entry => entry.InstanceA)
                .ThenBy(entry => entry.InstanceB)
                .Take(limit)
                .ToList();

            foreach (var entry in shown) Confirm(entry, baseScore, layouts, faster);

            // 다시 푼 값으로 순서가 뒤집힐 수 있다. 보여주는 것은 다시 푼 값이므로 줄도 그것으로 세운다.
            return shown
                .OrderByDescending(entry => entry.Gain)
                .ThenBy(entry => entry.InstanceA)
                .ThenBy(entry => entry.InstanceB)
                .ToList();
        }

        /// <summary>
        /// 짐작으로 뽑힌 쌍을 제대로 푼다. 기준 점수가 배치 후보 전부에서 나왔으므로 증가분도
        /// 같은 자리에서 나와야 뜻이 있다.
        ///
        /// 회전도 여기서 확정된다. 효과가 같은 회전이 여럿일 때 짐작 단계가 고른 것이 그대로
        /// 오는데, 어느 쪽이든 결과 질의가 같으므로 따라 해도 같은 석판이 나온다.
        /// </summary>
        private static void Confirm(
            MixAdvice advice, double baseScore, LayoutCache layouts, SolverOptions faster)
        {
            var trial = advice.Trial;
            if (trial is null) return;

            var solved = PlacementSolver.EvaluateLayouts(trial, layouts.Of(trial, faster), faster);
            advice.Gain = solved.Score - baseScore;
            advice.Effect = EffectOf(trial, solved);
        }

        /// <summary>한 쌍에서 가장 좋은 회전 조합. 회전마다 결과 질의가 달라 값도 달라진다.</summary>
        private static MixAdvice? Best(
            PlacementProblem problem,
            (MixMaterial Material, TabletSlot Slot) a,
            (MixMaterial Material, TabletSlot Slot) b,
            TabletDefinition mixedDefinition,
            double baseScore,
            LayoutCache layouts,
            IReadOnlyList<TabletPlacement>? baseLayout,
            int indexA,
            int indexB,
            SolverOptions faster)
        {
            MixAdvice? best = null;

            foreach (var rotationA in RotationsOf(a.Material, b.Material))
            {
                foreach (var rotationB in TabletMix.Rotations(b.Material))
                {
                    var mixed = TabletMix.Of(a.Material, rotationA, b.Material, rotationB);
                    if (mixed is null) continue;

                    var trial = Without(problem, a.Material.InstanceId, b.Material.InstanceId);
                    var mixedSlot = new TabletSlot
                    {
                        Definition = mixedDefinition,
                        InstanceId = -1,
                        InstanceQuery = mixed.Query,
                        InstanceConditionQuery = mixed.ConditionQuery.Length > 0 ? mixed.ConditionQuery : null,
                        Rotatable = mixed.Rotatable,
                    };
                    trial.Tablets.Add(mixedSlot);

                    var guesses = Guesses(baseLayout, indexA, indexB, mixedSlot);
                    var solved = PlacementSolver.EvaluateLayouts(
                        trial, guesses ?? layouts.Of(trial, faster), faster);
                    var gain = solved.Score - baseScore;
                    if (best is not null && gain <= best.Gain) continue;

                    best = new MixAdvice
                    {
                        Trial = trial,
                        InstanceA = a.Material.InstanceId,
                        InstanceB = b.Material.InstanceId,
                        NameA = Name(a.Slot),
                        NameB = Name(b.Slot),
                        RotationA = rotationA,
                        RotationB = rotationB,
                        Gain = gain,
                        Rotatable = mixed.Rotatable,
                        Query = mixed.Query,
                        Effect = EffectOf(trial, solved),
                    };
                }
            }
            return best;
        }

        /// <summary>
        /// 짐작용 배치. 지금 배치에서 재료 둘을 빼고, 그 두 빈자리에 결과를 놓아 본다.
        ///
        /// 자리는 <c>problem.Tablets</c> 순서와 짝지어져 있고, 합성 판은 재료 둘을 뺀 뒤 결과를
        /// 맨 뒤에 붙이므로, 빼고 붙이는 순서를 그대로 따르면 짝이 맞는다.
        /// 바탕이 없으면(석판이 다 놓이지 못한 판) null 을 돌려 제대로 풀게 한다.
        /// </summary>
        private static List<List<TabletPlacement>>? Guesses(
            IReadOnlyList<TabletPlacement>? baseLayout, int indexA, int indexB, TabletSlot mixed)
        {
            if (baseLayout is null) return null;
            if (indexA < 0 || indexB < 0 || indexA >= baseLayout.Count || indexB >= baseLayout.Count) return null;

            var kept = new List<TabletPlacement>(baseLayout.Count - 2);
            for (var i = 0; i < baseLayout.Count; i++)
            {
                if (i == indexA || i == indexB) continue;
                kept.Add(baseLayout[i]);
            }

            var guesses = new List<List<TabletPlacement>>(2);
            foreach (var cell in new[] { baseLayout[indexA].Position, baseLayout[indexB].Position })
            {
                var layout = new List<TabletPlacement>(kept) { mixed.At(cell, 0) };
                guesses.Add(layout);
            }
            return guesses;
        }

        /// <summary>
        /// 둘 다 돌릴 수 있으면 결과도 돌릴 수 있으므로, 절대 회전이 아니라 둘 사이의 각도만
        /// 결과를 가른다. 그때는 한쪽을 고정해 같은 모양을 네 번 풀지 않는다.
        /// </summary>
        private static IEnumerable<int> RotationsOf(MixMaterial a, MixMaterial b) =>
            a.Rotatable && b.Rotatable ? new[] { 0 } : TabletMix.Rotations(a);

        private static TabletEffectSummary EffectOf(PlacementProblem trial, Arrangement solved)
        {
            var index = trial.Tablets.Count - 1;
            if (index < 0 || index >= solved.Tablets.Count) return new TabletEffectSummary();

            var placement = solved.Tablets[index];
            return TabletEffectSummary.Of(placement.Query, trial.Grid, placement.Position, placement.Rotation);
        }

        private static List<(MixMaterial Material, TabletSlot Slot)> Materials(PlacementProblem problem)
        {
            var materials = new List<(MixMaterial, TabletSlot)>();
            foreach (var slot in problem.Tablets)
            {
                if (slot.Definition.EntityId == TabletMix.ResultEntityId) continue;

                var rotation = problem.CurrentTablets.TryGetValue(slot.InstanceId, out var current)
                    ? current.Rotation
                    : 0;

                materials.Add((
                    new MixMaterial(
                        slot.InstanceId,
                        slot.Definition.EntityId,
                        slot.InstanceQuery ?? slot.Definition.Query,
                        slot.InstanceConditionQuery ?? slot.Definition.ConditionQuery,
                        slot.Rotatable,
                        rotation),
                    slot));
            }
            return materials;
        }

        private static string Name(TabletSlot slot) =>
            !string.IsNullOrEmpty(slot.InstanceName)
                ? slot.InstanceName!
                : Naming.Of(slot.Definition.Names, slot.Definition.Id, "석판");

        /// <summary>
        /// 재료 둘을 뺀 문제. 현재 배치 정보도 함께 빼야, 이제 없는 석판의 자리를 지키려 들지 않는다.
        /// </summary>
        private static PlacementProblem Without(PlacementProblem problem, int first, int second)
        {
            var clone = new PlacementProblem
            {
                Grid = problem.Grid,
                Charms = new List<CharmSlot>(problem.Charms),
                Tablets = problem.Tablets.Where(t => t.InstanceId != first && t.InstanceId != second).ToList(),
                FixedTablets = problem.FixedTablets,
                FixedEffects = problem.FixedEffects,
                ComboCounts = problem.ComboCounts,
                Combos = problem.Combos,
                PriorityCategories = problem.PriorityCategories,
            };

            foreach (var pair in problem.CurrentTablets)
            {
                if (pair.Key == first || pair.Key == second) continue;
                clone.CurrentTablets[pair.Key] = pair.Value;
            }
            foreach (var pair in problem.CurrentCharms) clone.CurrentCharms[pair.Key] = pair.Value;
            foreach (var pair in problem.PlannedTablets)
            {
                if (pair.Key == first || pair.Key == second) continue;
                clone.PlannedTablets[pair.Key] = pair.Value;
            }
            foreach (var pair in problem.PlannedCharms) clone.PlannedCharms[pair.Key] = pair.Value;
            return clone;
        }
    }
}
