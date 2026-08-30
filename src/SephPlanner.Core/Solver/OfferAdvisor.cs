using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Model;
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

        public static List<OfferAdvice> Rank(
            PlacementProblem problem, double baseScore, IReadOnlyList<OfferCandidate> candidates, int gold)
        {
            var advice = new List<OfferAdvice>();
            var nextInstanceId = -1;

            foreach (var candidate in candidates)
            {
                var trial = Clone(problem);

                if (candidate.Charm is not null)
                {
                    trial.Charms.Add(new CharmSlot
                    {
                        Definition = candidate.Charm,
                        InstanceId = nextInstanceId--,
                        IsDormant = candidate.CharmIsDormant,
                    });
                }
                else if (candidate.Tablet is not null)
                {
                    trial.Tablets.Add(new TabletSlot
                    {
                        Definition = candidate.Tablet,
                        InstanceId = nextInstanceId--,
                    });
                }
                else
                {
                    continue;
                }

                var solved = PlacementSolver.Solve(trial, Faster);
                advice.Add(new OfferAdvice
                {
                    Candidate = candidate,
                    Gain = solved.Score - baseScore,
                    Affordable = candidate.Price <= gold,
                    Effect = EffectOf(candidate, trial, solved),
                });
            }

            // 살 수 없는 것은 아무리 좋아도 지금 고를 수 없다. 지우지는 않고 아래로 내린다.
            // 증가분까지 같으면 여력이 큰 쪽을 위로 올린다. 아티팩트가 적을 때는 여러 석판이
            // 똑같이 최대치를 뽑아내 증가분만으로는 우열이 드러나지 않는다.
            return advice.OrderByDescending(entry => entry.Affordable)
                         .ThenByDescending(entry => entry.Gain)
                         .ThenByDescending(entry => entry.Effect.Reach)
                         .ToList();
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

        private static PlacementProblem Clone(PlacementProblem problem) => new()
        {
            Grid = problem.Grid,
            Charms = new List<CharmSlot>(problem.Charms),
            Tablets = new List<TabletSlot>(problem.Tablets),
            FixedTablets = problem.FixedTablets,
        };
    }
}
