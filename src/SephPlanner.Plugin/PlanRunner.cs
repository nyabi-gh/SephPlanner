using System;
using System.Threading;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Planning;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임 안에서 배치를 푼다. 솔버는 빔 서치라 무거워서 게임 루프에서 돌리면 프레임이 끊기므로
    /// 백그라운드 스레드로 넘긴다.
    ///
    /// 넘겨도 되는 근거는 두 가지다. <see cref="SephPlanner.Core"/>는 유니티 객체를 건드리지 않는
    /// 순수 계산이고, <see cref="GameReader.Read"/>가 호출마다 새 스냅샷을 만들어 돌려주므로
    /// 푸는 동안 메인 스레드가 같은 객체를 고쳐 쓰는 일이 없다.
    ///
    /// 한 번에 하나만 돈다. 폴링이 풀이보다 빠를 때 요청이 쌓이면 게임이 스레드에 잠식된다.
    /// </summary>
    internal sealed class PlanRunner
    {
        private readonly ICatalog _catalog;
        private int _busy;

        private volatile Plan _latest;
        private volatile string _error;
        private volatile PlanBlocker _blocker;

        public PlanRunner(ICatalog catalog)
        {
            _catalog = catalog;
        }

        /// <summary>마지막으로 풀린 배치. 아직 한 번도 못 풀었으면 null 이다.</summary>
        public Plan Latest => _latest;

        /// <summary>마지막 풀이가 실패했으면 그 이유. 성공하면 지워진다.</summary>
        public string Error => _error;

        /// <summary>
        /// 배치가 안 나왔으면 왜 안 나왔는지. 아직 한 번도 안 돌았을 때도
        /// <see cref="PlanBlocker.None"/> 이므로, 화면은 <see cref="Latest"/>가 없는 것과 함께 본다.
        /// </summary>
        public PlanBlocker Blocker => _blocker;

        /// <summary>
        /// 설정은 풀 때마다 새로 받는다. 설정 탭에서 바뀐 값이 다음 풀이부터 곧바로 걸리고,
        /// 백그라운드 스레드가 읽는 동안 메인 스레드가 같은 것을 고치는 일도 없다.
        /// </summary>
        /// <summary>받아들였으면 참. 이미 풀고 있는 중이면 거짓이고, 그때는 부른 쪽이 다시 내야 한다.</summary>
        public bool Submit(GameSnapshot snapshot, PlanPreferences preferences)
        {
            if (snapshot == null) return false;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // 직전 계획을 앵커로 넘긴다. 없으면 동점 배치 사이에서 목표가 걸음마다 뒤바뀐다.
                    _latest = PlanBuilder.Build(snapshot, _catalog, preferences, out var blocker, _latest);
                    _blocker = blocker;
                    _error = null;
                }
                catch (Exception ex)
                {
                    // 여기서 던지면 스레드풀 스레드가 죽으면서 게임까지 내린다. 이유만 남기고 만다.
                    _error = ex.Message;
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            });
            return true;
        }
    }
}
