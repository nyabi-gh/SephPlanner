using System;
using System.Collections.Generic;

namespace SephPlanner.Core.Runtime
{
    public sealed class AutomaticDiagnosticPolicy
    {
        // 실측 수집량이 없는 초기 운영 상한이다. 같은 실행에서는 동일 오류를 한 번만 수집한다.
        public const int SessionLimit = 10;
        public const string DisclosureVersion = "automatic-v1";
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        public static string ConsentKey(Uri endpoint) => DisclosureVersion + "|" + DiagnosticUploadClient.ConsentKey(endpoint);
        public bool TryAccept(string key, DiagnosticUploadThrottle throttle)
        {
            var shape = Shape(key);
            if (_seen.Count >= SessionLimit || _seen.Contains(shape) || !throttle.TryStart(out _)) return false;
            return _seen.Add(shape);
        }

        /// <summary>
        /// 같은 오류를 세션당 한 번만 보내려면 좌표와 수치만 다른 문장을 같은 것으로 봐야 한다.
        /// 칸마다 새 문장이 되던 시뮬레이션 불일치가 한 세션에서 열 번까지 올라갔다.
        /// </summary>
        public static string Shape(string detail)
        {
            var text = new System.Text.StringBuilder(detail.Length);
            var digits = false;
            foreach (var letter in detail)
            {
                if (letter >= '0' && letter <= '9')
                {
                    if (!digits) text.Append('#');
                    digits = true;
                    continue;
                }
                digits = false;
                text.Append(letter);
            }
            return WithoutRepeats(text.ToString());
        }

        private const int LongestRepeatedPiece = 6;

        /// <summary>
        /// 칸을 늘어놓는 문장은 칸마다 같은 조각이 되풀이된다. 숫자만 지우면 칸이 넷인 문장과 하나인
        /// 문장이 다른 오류가 되어, 한 원인이 칸 수가 바뀔 때마다 다시 올라갔다(제보 6d50beb6·fa581f2a·
        /// 990421e4). 바로 앞 조각을 되풀이하는 조각은 하나로 접는다.
        /// </summary>
        private static string WithoutRepeats(string shape)
        {
            var kept = new List<string>();
            foreach (var word in shape.Split(' '))
            {
                kept.Add(word);
                for (var length = 1; length <= LongestRepeatedPiece && length * 2 <= kept.Count; length++)
                {
                    if (!EndsWithRepeat(kept, length)) continue;
                    kept.RemoveRange(kept.Count - length, length);
                    break;
                }
            }
            return string.Join(" ", kept);
        }

        private static bool EndsWithRepeat(List<string> words, int length)
        {
            var start = words.Count - length;
            for (var i = 0; i < length; i++)
                if (!string.Equals(words[start + i], words[start - length + i], StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
