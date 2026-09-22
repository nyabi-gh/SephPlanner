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
            return text.ToString();
        }
    }
}
