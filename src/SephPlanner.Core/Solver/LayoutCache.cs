using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 석판 배치 탐색을 돌려 쓰는 자리.
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
    ///
    /// <b>계획 하나보다 오래 산다.</b> 지문이 아이템 자리까지 보므로 아티팩트를 하나 옮기기만
    /// 해도 판이 다시 풀리는데, 석판이 그대로면 빔은 같은 것을 다시 찾을 뿐이다. 그래서
    /// <c>PlanRunner</c>가 이것을 들고 계획마다 넘긴다. 계획 맥락(카탈로그 세대와 설정)이 바뀌면
    /// 들고 있는 쪽이 캐시를 통째로 새로 짓는다 - 그래서 그 둘은 열쇠에 없다.
    ///
    /// <b>계획을 넘어가는 것은 탐색뿐이고 채점은 아니다.</b> 지금 배치와 직전 계획은 폴링마다
    /// 달라지므로, 빔에 붙이는 그 둘은 <see cref="PlacementSolver.WithCurrentAndPlanned"/>가
    /// 부를 때마다 다시 짓고 채점 결과는 <see cref="BeginPlan"/>이 버린다.
    ///
    /// <b>열쇠가 둘이다.</b> 빔이 <c>SolverOptions</c> 에서 읽는 것은 폭 셋뿐이라, 다듬기 횟수나
    /// 탐색 예산까지 한 열쇠에 묶으면 <b>같은 빔을 찾는 조언들이 칸을 나눠 갖는다</b> -
    /// <see cref="DiscardAdvisor"/>가 <c>ForAdvice</c>의 예산 둘을 덮어쓰기 때문에 실제로 그랬다.
    /// 그래서 빔은 빔 열쇠로, 기준 배치와 잣대는 그것을 품은 채점 열쇠로 찾는다.
    ///
    /// <b>"쓸 수 있다"이지 "같다"가 아니다.</b> 빔도 석판 구성만의 함수는 아니다 -
    /// <c>Estimate</c>가 현재·직전 자리를 동률 가르기에 읽는다. 보장하는 것은 모든 후보가 그
    /// 판으로 정확히 채점되고 지금 배치와 직전 계획이 매번 다시 붙는다는 것까지다.
    /// </summary>
    public sealed class LayoutCache
    {
        /// <summary>
        /// 들고 있을 석판 구성의 수. 석판을 줍고, 합성을 권하고, 상자를 열 때마다 가상 구성이
        /// 하나씩 생기므로 상한이 없으면 세션 내내 부푼다. 다음 계획이 반드시 다시 보는 것은
        /// 지금 가방의 구성이라, 가장 오래 안 쓴 것부터 버려도 값은 다 한다.
        /// </summary>
        private const int Limit = 192;

        private sealed class Entry
        {
            public List<List<TabletPlacement>>? Value;
            public long Used;
        }

        /// <summary>빔 열쇠로 찾는 탐색 결과. 계획을 넘어 산다.</summary>
        private readonly Dictionary<string, Entry> _beams = new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>채점 열쇠로 찾는 잣대. <see cref="BeginPlan"/>가 계획마다 버린다.</summary>
        private readonly Dictionary<string, Entry> _yardsticks = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private long _clock;

        private PlacementProblem? _baselineProblem;
        private string _baselineKey = "";
        private Arrangement? _baseline;

        /// <summary>실제로 돈 탐색 횟수. 돌려 쓰기가 듣고 있는지 재는 자리다.</summary>
        public int Searches { get; private set; }

        /// <summary>돌려 쓴 횟수. 테스트가 이 둘로 비용을 확인한다.</summary>
        public int Reuses { get; private set; }

        /// <summary>
        /// 새 계획을 시작한다. 빔은 두고 채점한 것만 버린다 - 기준 배치와 잣대는 지금 배치와
        /// 직전 계획을 보고 고른 배치라, 넘겨 쓰면 그때의 앵커로 이번 답을 고르게 된다.
        /// </summary>
        public void BeginPlan()
        {
            _baseline = null;
            _baselineProblem = null;
            _baselineKey = "";
            _yardsticks.Clear();
        }

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
            var key = ScoringKey(problem, options);
            if (_baseline is not null && ReferenceEquals(_baselineProblem, problem) && _baselineKey == key)
            {
                Reuses++;
                return _baseline;
            }

            // 남의 판을 붙들고 있지 않도록 먼저 놓는다.
            _baseline = null;
            _baselineProblem = null;
            _baselineKey = "";

            var solved = PlacementSolver.EvaluateLayouts(problem, Of(problem, options), options);
            _baselineProblem = problem;
            _baselineKey = key;
            _baseline = solved;
            return solved;
        }

        public List<List<TabletPlacement>> Of(PlacementProblem problem, SolverOptions options)
        {
            var key = BeamKey(problem, options);
            var beam = Find(_beams, key)?.Value;
            if (beam is not null) Reuses++;
            else
            {
                Searches++;
                beam = PlacementSolver.SearchBeam(problem, options);
                Reserve(_beams, key).Value = beam;
            }
            return PlacementSolver.WithCurrentAndPlanned(problem, beam);
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
            var key = ScoringKey(problem, options);
            var cached = Find(_yardsticks, key)?.Value;
            if (cached is not null)
            {
                Reuses++;
                return cached;
            }

            var best = PlacementSolver.EvaluateLayouts(problem, Of(problem, options), options);
            var one = new List<List<TabletPlacement>> { new List<TabletPlacement>(best.Tablets) };
            Reserve(_yardsticks, key).Value = one;
            return one;
        }

        private Entry? Find(Dictionary<string, Entry> map, string key)
        {
            if (!map.TryGetValue(key, out var entry)) return null;
            entry.Used = ++_clock;
            return entry;
        }

        private Entry Reserve(Dictionary<string, Entry> map, string key)
        {
            if (map.TryGetValue(key, out var entry))
            {
                entry.Used = ++_clock;
                return entry;
            }

            if (map.Count >= Limit) DropOldest(map);
            entry = new Entry { Used = ++_clock };
            map.Add(key, entry);
            return entry;
        }

        private static void DropOldest(Dictionary<string, Entry> map)
        {
            var oldest = "";
            var used = long.MaxValue;
            foreach (var pair in map)
            {
                if (pair.Value.Used >= used) continue;
                used = pair.Value.Used;
                oldest = pair.Key;
            }
            map.Remove(oldest);
        }

        /// <summary>
        /// 빔 탐색이 읽는 것 전부. 석판 슬롯과 격자, 각인, 그리고 <b>탐색 폭</b>이다.
        ///
        /// 인스턴스 번호만으로는 모자란다. 합성 추천은 합쳐진 석판을 늘 번호 -1 로 넣고 회전
        /// 조합마다 질의가 다르기 때문에, 질의까지 세지 않으면 서로 다른 합성이 한 칸을 나눠 쓰게 된다.
        ///
        /// 계획 하나 안에서만 살 때는 없어도 되던 것 셋이 캐시가 오래 살면서 들어왔다. <b>지금
        /// 각도</b>(<c>DistinctRotations</c>가 여기서부터 세므로, 효과가 같은 회전이 여럿인 석판은
        /// 각도가 달라지면 후보에 담기는 회전값 자체가 달라진다), <b>각인과 고정 칸 효과</b>(옮길
        /// 수는 없어도 효과는 내고, 신비 각인은 콤보 수를 따라 런 중에 질의가 바뀐다), 그리고
        /// <b>한 부모의 몫</b>(빔에 무엇을 남길지를 가른다)이다.
        ///
        /// <b>채점 강도는 여기 없다.</b> <c>SearchBeam</c> 이 <c>SolverOptions</c> 에서 읽는 것은
        /// <c>BeamWidth</c>·<c>ExactCandidates</c>·<c>ParentQuota</c> 뿐이고, 다듬기 횟수나 수렴
        /// 반복, 탐색 예산은 <c>Evaluate</c> 쪽에서만 쓰인다. 우선 카테고리도 마찬가지로
        /// <c>PriorityComboPlacement</c> 가 채점할 때만 본다. 그것들을 빔 열쇠에 두면 강도만 다른
        /// 조언들이 같은 빔을 두 번 찾는다 - <see cref="DiscardAdvisor"/> 가 <c>ForAdvice</c> 의
        /// 예산 둘을 덮어쓰기 때문에 실제로 그랬다.
        /// </summary>
        private static string BeamKey(PlacementProblem problem, SolverOptions options) =>
            Key(problem, options, new StringBuilder());

        /// <summary>
        /// 빔 열쇠에 채점을 가르는 것을 더한 열쇠. 기준 배치와 잣대가 이것으로 찾는다.
        /// 빔 열쇠를 통째로 품으므로 채점 열쇠가 같으면 빔 열쇠도 반드시 같다.
        /// </summary>
        private static string ScoringKey(PlacementProblem problem, SolverOptions options)
        {
            var builder = new StringBuilder();
            builder.Append(options.FixpointIterations)
                   .Append('/').Append(options.PolishPasses)
                   .Append('/').Append(options.PriorityComboTrials)
                   .Append('/').Append(options.EmptySideTrials).Append(';');
            foreach (var category in problem.PriorityCategories.OrderBy(value => value, StringComparer.Ordinal))
                builder.Append("priority:").Append(category.Length).Append(':').Append(category).Append(';');
            return Key(problem, options, builder);
        }

        private static string Key(PlacementProblem problem, SolverOptions options, StringBuilder builder)
        {
            builder.Append(problem.Grid.Width).Append('x').Append(problem.Grid.Height)
                   .Append('/').Append(problem.Grid.Storage)
                   .Append('/').Append(options.BeamWidth)
                   .Append('/').Append(options.ExactCandidates)
                   .Append('/').Append(options.ParentQuota).Append(';');

            foreach (var slot in problem.Tablets)
            {
                builder.Append(slot.InstanceId).Append(':')
                       .Append(slot.Definition.EntityId).Append(':')
                       .Append(slot.Rotatable ? '1' : '0').Append(':')
                       .Append(PlacementSolver.CurrentRotation(problem, slot)).Append(':')
                       .Append(slot.InstanceQuery ?? "").Append(':')
                       .Append(slot.InstanceConditionQuery ?? "").Append(';');
            }
            foreach (var engraving in problem.FixedTablets)
                builder.Append("engraving:").Append(engraving.Definition.EntityId).Append(':')
                    .Append(engraving.Position.X).Append(',').Append(engraving.Position.Y).Append(':')
                    .Append(engraving.Rotation).Append(':')
                    .Append(engraving.Query.Length).Append(':').Append(engraving.Query)
                    .Append(engraving.ConditionQuery.Length).Append(':').Append(engraving.ConditionQuery).Append(';');
            foreach (var effect in problem.FixedEffects)
                builder.Append("fixed:").Append(effect.Position.X).Append(',').Append(effect.Position.Y).Append(':')
                    .Append(effect.Level).Append(':').Append(effect.Multiply).Append(':')
                    .Append(effect.Disable).Append(':').Append(effect.IgnoreCriteria).Append(';');
            foreach (var charm in problem.Charms.Where(charm => ScalesPosition.Required(problem, charm)).OrderBy(charm => charm.InstanceId))
                builder.Append("scales:").Append(charm.InstanceId).Append(':')
                    .Append(ScalesPosition.IsLeft(problem.CurrentCharms[charm.InstanceId]) ? 'L' : 'R').Append(';');

            // 못 옮기는 아이템은 그 칸을 석판에게서 빼앗는다. 열쇠에 없으면 그 아이템이 사라진
            // 뒤에도 옛 빔을 돌려받아 못 쓰던 칸을 계속 비워 둔다.
            foreach (var charm in problem.Charms.Where(charm => charm.Immovable).OrderBy(charm => charm.InstanceId))
            {
                builder.Append("pinned:").Append(charm.InstanceId).Append(':');
                if (problem.CurrentCharms.TryGetValue(charm.InstanceId, out var at))
                    builder.Append(at.X).Append(',').Append(at.Y);
                builder.Append(';');
            }
            return builder.ToString();
        }
    }
}
