using System.Collections.Generic;
using System.Linq;
using System.Text;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 한 번의 계획 안에서 석판 배치 탐색을 돌려 쓰는 자리.
    ///
    /// <b>왜 필요한가.</b> 후보 추천은 "이것을 집으면 얼마나 좋아지나"를 판을 통째로 다시 풀어서
    /// 답한다. 원칙은 옳지만, <b>아티팩트 후보는 석판을 하나도 건드리지 않는다</b>. 그런데도
    /// 후보마다, 그리고 가방이 찼을 때는 밀려날 아티팩트마다 배치 탐색이 처음부터 다시 돌았다 -
    /// 실측에서 42칸 가방이 꽉 찬 채 후보 8개를 보면 탐색이 288번 돌아 41초, 할당 48GB였다.
    /// 같은 석판 구성이면 탐색 결과도 같으므로, 여기서 한 번만 짓고 나눠 준다.
    ///
    /// <b>같음의 기준은 석판 구성이다.</b> 아티팩트가 달라지면 빔을 좁히는 어림값
    /// (<c>scoring</c>·<c>levelCap</c>)이 조금 달라질 수 있지만, 그것은 후보를 줄 세우는 값일
    /// 뿐 점수가 아니다. 채점은 언제나 그 배치로 다시 정확히 하므로 보고되는 점수는 실제 배치의
    /// 점수 그대로이고, 기준과 후보가 같은 배치 후보들 위에서 겨루게 되어 증가분의 공정함은
    /// 오히려 좋아진다.
    /// </summary>
    public sealed class LayoutCache
    {
        private readonly Dictionary<string, List<List<TabletPlacement>>> _layouts =
            new Dictionary<string, List<List<TabletPlacement>>>();

        private readonly Dictionary<string, List<List<TabletPlacement>>> _yardsticks =
            new Dictionary<string, List<List<TabletPlacement>>>();

        private PlacementProblem? _baselineProblem;
        private string _baselineKey = "";
        private Arrangement? _baseline;

        /// <summary>실제로 돈 탐색 횟수. 돌려 쓰기가 듣고 있는지 재는 자리다.</summary>
        public int Searches { get; private set; }

        /// <summary>돌려 쓴 횟수. 테스트가 이 둘로 비용을 확인한다.</summary>
        public int Reuses { get; private set; }

        /// <summary>
        /// 후보를 하나도 집지 않은 지금 판의 배치.
        ///
        /// <b>세 곳이 같은 것을 봐야 한다.</b> 후보 추천과 석판 합성은 이것을 증가분의 기준으로
        /// 삼고, 미리보기의 "달라지는 칸"도 이것과 견줘야 한다 - 다른 강도로 푼 배치와 견주면
        /// 두 탐색이 동점 배치를 다르게 골라, 후보 때문이 아닌 칸이 달라진 것으로 나온다.
        /// 예전에는 두 조언이 이것을 따로 풀어 같은 계산을 두 번 했고, 미리보기는 아예 다른
        /// 강도의 배치와 견주고 있었다.
        ///
        /// 기억해 둔 것은 판도 탐색 강도도 그대로일 때만 돌려준다. 어느 한쪽이라도 다르면 조용히
        /// 남의 답을 받는 대신 다시 푼다 - 기준과 후보가 다른 잣대로 풀리는 것이 애초에 막으려는
        /// 일이므로, 여기서 그것을 되살리면 안 된다.
        /// </summary>
        public Arrangement Baseline(PlacementProblem problem, SolverOptions options)
        {
            var key = Key(problem, options);
            if (_baseline is not null && ReferenceEquals(_baselineProblem, problem) && _baselineKey == key)
            {
                Reuses++;
                return _baseline;
            }

            _baselineProblem = problem;
            _baselineKey = key;
            _baseline = PlacementSolver.EvaluateLayouts(problem, Of(problem, options), options);
            return _baseline;
        }

        public List<List<TabletPlacement>> Of(PlacementProblem problem, SolverOptions options)
        {
            var key = Key(problem, options);
            if (_layouts.TryGetValue(key, out var cached))
            {
                Reuses++;
                return cached;
            }

            Searches++;
            var layouts = PlacementSolver.SearchLayouts(problem, options);
            _layouts[key] = layouts;
            return layouts;
        }

        /// <summary>
        /// 이 석판 구성을 견줄 때 쓸 배치 하나.
        ///
        /// 가방이 찼을 때 후보 하나를 집는 갈래는 "무엇을 밀어내느냐"로 갈리는데, 갈래마다 배치
        /// 후보 수십 개를 전부 채점하면 같은 배치를 수십 번 다시 고르게 된다. <b>갈래를 가르는
        /// 것은 배치가 아니다.</b> 그래서 견줄 때는 이 하나로 견주고, 이긴 갈래만 배치 후보
        /// 전부로 다시 풀어 보고할 점수를 낸다.
        /// </summary>
        public IReadOnlyList<List<TabletPlacement>> Yardstick(
            PlacementProblem problem, SolverOptions options)
        {
            var key = Key(problem, options);
            if (_yardsticks.TryGetValue(key, out var cached))
            {
                Reuses++;
                return cached;
            }

            var best = PlacementSolver.EvaluateLayouts(problem, Of(problem, options), options);
            var one = new List<List<TabletPlacement>> { new List<TabletPlacement>(best.Tablets) };
            _yardsticks[key] = one;
            return one;
        }

        /// <summary>
        /// 탐색 결과를 가르는 것 전부. 석판 슬롯과 격자, 그리고 탐색 강도다.
        ///
        /// 인스턴스 번호만으로는 모자란다. 합성 추천은 합쳐진 석판을 늘 번호 -1 로 넣고 회전
        /// 조합마다 질의가 다르기 때문에, 질의까지 세지 않으면 서로 다른 합성이 한 칸을 나눠 쓰게 된다.
        /// </summary>
        private static string Key(PlacementProblem problem, SolverOptions options)
        {
            var builder = new StringBuilder();
            builder.Append(problem.Grid.Width).Append('x').Append(problem.Grid.Height)
                   .Append('/').Append(problem.Grid.Storage)
                   .Append('/').Append(options.BeamWidth)
                   .Append('/').Append(options.ExactCandidates)
                   .Append('/').Append(options.FixpointIterations)
                   .Append('/').Append(options.PolishPasses)
                   .Append('/').Append(options.PriorityComboTrials).Append(';');

            foreach (var slot in problem.Tablets)
            {
                builder.Append(slot.InstanceId).Append(':')
                       .Append(slot.Definition.EntityId).Append(':')
                       .Append(slot.Rotatable ? '1' : '0').Append(':')
                       .Append(slot.InstanceQuery ?? "").Append(':')
                       .Append(slot.InstanceConditionQuery ?? "").Append(';');
            }
            foreach (var category in problem.PriorityCategories.OrderBy(value => value, System.StringComparer.Ordinal))
                builder.Append("priority:").Append(category.Length).Append(':').Append(category).Append(';');
            return builder.ToString();
        }
    }
}
