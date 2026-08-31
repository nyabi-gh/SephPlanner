using System.Collections.Generic;
using System.Linq;
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
        /// <summary>후보 추천과 같은 탐색 강도. 기준과 후보를 같은 잣대로 풀어야 증가분이 뜻을 갖는다.</summary>
        private static readonly SolverOptions Faster = new() { BeamWidth = 150, ExactCandidates = 40 };

        public static List<MixAdvice> Rank(
            PlacementProblem problem, ICatalog catalog, int cost, int gold, int limit = 5)
        {
            var materials = Materials(problem);
            if (materials.Count < 2) return new List<MixAdvice>();

            var baseScore = PlacementSolver.Solve(problem, Faster).Score;
            var mixedDefinition = TabletMix.Definition(catalog.Tablet(TabletMix.ResultEntityId));

            var advice = new List<MixAdvice>();
            for (var i = 0; i < materials.Count; i++)
            {
                for (var j = i + 1; j < materials.Count; j++)
                {
                    var best = Best(problem, materials[i], materials[j], mixedDefinition, baseScore);
                    if (best is not null) advice.Add(best);
                }
            }

            foreach (var entry in advice) entry.Affordable = cost <= gold;

            // 증가분이 같은 쌍이 여럿일 때 인스턴스 번호로 갈라야 제안이 흔들리지 않는다.
            return advice
                .OrderByDescending(entry => entry.Gain)
                .ThenBy(entry => entry.InstanceA)
                .ThenBy(entry => entry.InstanceB)
                .Take(limit)
                .ToList();
        }

        /// <summary>한 쌍에서 가장 좋은 회전 조합. 회전마다 결과 질의가 달라 값도 달라진다.</summary>
        private static MixAdvice? Best(
            PlacementProblem problem,
            (MixMaterial Material, TabletSlot Slot) a,
            (MixMaterial Material, TabletSlot Slot) b,
            TabletDefinition mixedDefinition,
            double baseScore)
        {
            MixAdvice? best = null;

            foreach (var rotationA in RotationsOf(a.Material, b.Material))
            {
                foreach (var rotationB in TabletMix.Rotations(b.Material))
                {
                    var mixed = TabletMix.Of(a.Material, rotationA, b.Material, rotationB);
                    if (mixed is null) continue;

                    var trial = Without(problem, a.Material.InstanceId, b.Material.InstanceId);
                    trial.Tablets.Add(new TabletSlot
                    {
                        Definition = mixedDefinition,
                        InstanceId = -1,
                        InstanceQuery = mixed.Query,
                        InstanceConditionQuery = mixed.ConditionQuery.Length > 0 ? mixed.ConditionQuery : null,
                        Rotatable = mixed.Rotatable,
                    });

                    var solved = PlacementSolver.Solve(trial, Faster);
                    var gain = solved.Score - baseScore;
                    if (best is not null && gain <= best.Gain) continue;

                    best = new MixAdvice
                    {
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
            };

            foreach (var pair in problem.CurrentTablets)
            {
                if (pair.Key == first || pair.Key == second) continue;
                clone.CurrentTablets[pair.Key] = pair.Value;
            }
            foreach (var pair in problem.CurrentCharms) clone.CurrentCharms[pair.Key] = pair.Value;
            return clone;
        }
    }
}
