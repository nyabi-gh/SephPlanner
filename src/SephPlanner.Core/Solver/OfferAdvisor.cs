using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Model;

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
    }

    public sealed class OfferAdvice
    {
        public OfferCandidate Candidate { get; set; } = new OfferCandidate();
        public double Gain { get; set; }
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
            PlacementProblem problem, double baseScore, IReadOnlyList<OfferCandidate> candidates)
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
                advice.Add(new OfferAdvice { Candidate = candidate, Gain = solved.Score - baseScore });
            }

            return advice.OrderByDescending(entry => entry.Gain).ToList();
        }

        private static PlacementProblem Clone(PlacementProblem problem) => new()
        {
            Grid = problem.Grid,
            Charms = new List<CharmSlot>(problem.Charms),
            Tablets = new List<TabletSlot>(problem.Tablets),
        };
    }
}
