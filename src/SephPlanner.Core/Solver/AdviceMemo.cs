using System;
using System.Collections.Generic;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 한 배치에 붙인 조언을 부분(합성·후보·버리기·인챈트)별로 기억한다.
    ///
    /// 조언은 배치가 그대로여도 세피라이트 창을 여닫거나 합성기·제단에 다가갈 때마다 처음부터 다시
    /// 돌았고, 돌던 것은 버렸다. 그런데 창을 여닫아 바뀌는 것은 후보뿐이라 버리기·합성·인챈트는 같은
    /// 답을 다시 냈다(제보 <c>46fe8f23</c>, 조언 1198회 중 배치 없이 다시 돈 것이 약 340회, 창 여닫이 406회).
    /// 같은 판에서 같은 입력으로 푼 부분은 다시 풀어도 같은 답이므로 기억해 둔 것을 쓴다. 판이 바뀌면
    /// <see cref="LayoutCache.BeginPlan"/>이 비운다.
    /// </summary>
    internal sealed class AdviceMemo
    {
        /// <summary>
        /// 부분마다 들고 있을 입력의 수. 창은 열림·닫힘을 오가므로 둘이면 되고, 사이에 낀 다른 변화
        /// (합성기에 다가가기 등)까지 넉넉히 둔다. 넘치면 비운다 - 다시 풀 뿐 틀리지는 않는다.
        /// </summary>
        private const int Limit = 4;

        private readonly Dictionary<string, Dictionary<string, object>> _parts =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);

        public void Clear() => _parts.Clear();

        /// <summary>
        /// 이 부분을 이 입력으로 푼 것이 있으면 그것을, 없으면 풀어서 기억한다. 푸는 도중 취소되면
        /// 아무것도 남기지 않는다.
        /// </summary>
        public T Get<T>(string part, string input, Func<T> solve) where T : class
        {
            if (!_parts.TryGetValue(part, out var kept))
                _parts[part] = kept = new Dictionary<string, object>(StringComparer.Ordinal);
            if (kept.TryGetValue(input, out var found)) return (T)found;

            var solved = solve();
            if (kept.Count >= Limit) kept.Clear();
            kept[input] = solved;
            return solved;
        }
    }
}
