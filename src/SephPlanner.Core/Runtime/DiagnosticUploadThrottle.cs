using System;
using System.Diagnostics;

namespace SephPlanner.Core.Runtime
{
    public sealed class DiagnosticUploadThrottle
    {
        // 연속 제보를 제한하는 초기 운영 정책값이며 실측 수집 간격은 아니다.
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);
        private readonly TimeSpan _interval;
        private readonly Func<TimeSpan> _elapsed;
        private readonly object _gate = new object();
        private TimeSpan? _lastStarted;

        public DiagnosticUploadThrottle(TimeSpan interval, Func<TimeSpan>? elapsed = null)
        {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            _interval = interval;
            if (elapsed == null)
            {
                var clock = Stopwatch.StartNew();
                _elapsed = () => clock.Elapsed;
            }
            else _elapsed = elapsed;
        }

        public bool TryStart(out TimeSpan remaining)
        {
            lock (_gate)
            {
                var now = _elapsed();
                remaining = _lastStarted.HasValue ? _interval - (now - _lastStarted.Value) : TimeSpan.Zero;
                if (remaining > TimeSpan.Zero) return false;
                _lastStarted = now;
                remaining = TimeSpan.Zero;
                return true;
            }
        }
    }
}
