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
        private readonly PlanPreferences _preferences;
        private int _busy;

        private volatile Plan _latest;
        private volatile string _error;

        public PlanRunner(ICatalog catalog, PlanPreferences preferences)
        {
            _catalog = catalog;
            _preferences = preferences;
        }

        /// <summary>마지막으로 풀린 배치. 아직 한 번도 못 풀었으면 null 이다.</summary>
        public Plan Latest => _latest;

        /// <summary>마지막 풀이가 실패했으면 그 이유. 성공하면 지워진다.</summary>
        public string Error => _error;

        public void Submit(GameSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _latest = PlanBuilder.Build(snapshot, _catalog, _preferences);
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
        }
    }
}
