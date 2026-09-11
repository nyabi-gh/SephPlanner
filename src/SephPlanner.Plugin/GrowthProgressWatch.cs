using System.Collections.Generic;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 성장 아티팩트의 진행도를 게임이 화면으로 보낼 때 받아 둔다.
    ///
    /// 진행도는 서버에만 있는 값이라 참가자 세션에서는 필드를 읽어도 서버의 값이 아니다. 게임이
    /// 소유자에게 보내는 <c>OnEffectHUDSetValue</c>가 유일한 길이고, <b>호스트에서도 같은 이벤트가
    /// 로컬로 발생하므로 싱글·호스트·참가자가 한 경로를 쓴다.</b>
    ///
    /// 값은 진행도가 오를 때뿐 아니라 효과가 켜질 때(<c>OnEnabledEffect</c>)도 다시 오므로, 아이템을
    /// 옮겨 효과가 갱신되면 그 자리에서 최신값이 들어온다. 다만 아직 한 번도 오지 않은 것을 0으로
    /// 지어내지는 않는다 - 모르는 것은 모르는 채로 둔다.
    /// </summary>
    internal static class GrowthProgressWatch
    {
        private static readonly Dictionary<int, int> Progress = new Dictionary<int, int>();
        private static UnitAvatar _watched;

        /// <summary>지금 보고 있는 아바타에 붙는다. 아바타가 바뀌면 쌓아 둔 값은 버린다.</summary>
        internal static void Follow(UnitAvatar avatar)
        {
            if (ReferenceEquals(_watched, avatar)) return;

            if (_watched != null)
            {
                _watched.OnEffectHUDSetValue -= Record;
                _watched.OnEffectHUDDestroyed -= Forget;
            }
            Progress.Clear();
            _watched = avatar;
            if (_watched == null) return;

            _watched.OnEffectHUDSetValue += Record;
            _watched.OnEffectHUDDestroyed += Forget;
        }

        internal static int? Of(int instanceId) =>
            Progress.TryGetValue(instanceId, out var progress) ? progress : (int?)null;

        internal static void Clear()
        {
            Follow(null);
        }

        private static void Record(string effectName, string value)
        {
            if (GrowthProgressReading.TryRead(effectName, value, out var instanceId, out var progress))
                Progress[instanceId] = progress;
        }

        private static void Forget(string effectName)
        {
            if (GrowthProgressReading.TryRead(effectName, "0", out var instanceId, out _))
                Progress.Remove(instanceId);
        }
    }
}
