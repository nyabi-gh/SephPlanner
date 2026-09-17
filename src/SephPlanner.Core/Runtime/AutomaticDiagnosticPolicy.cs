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
            if (_seen.Count >= SessionLimit || _seen.Contains(key) || !throttle.TryStart(out _)) return false;
            return _seen.Add(key);
        }
    }
}
