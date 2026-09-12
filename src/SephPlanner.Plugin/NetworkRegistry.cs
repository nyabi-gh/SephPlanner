using System.Collections.Generic;
using Mirror;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 씬에 있는 특정 컴포넌트 전부를, <b>씬을 훑지 않고</b> Mirror 의 등록부에서 읽는다.
    ///
    /// <b>왜.</b> 계측이 답을 줬다(제보 <c>6961d1a0</c>) - 상자·상점 찾기 2,697ms 가운데
    /// <c>FindObjectsByType</c> 한 줄이 2,671ms(99%)였다. 우리가 찾는 셋
    /// (<c>GridInventory</c>·<c>Sephirite</c>·<c>TabletMix</c>)이 <b>모두 NetworkBehaviour</b> 라,
    /// Mirror 의 <c>spawned</c>(번호 → <c>NetworkIdentity</c>)와 각 아이덴티티가 캐시해 둔
    /// <c>NetworkBehaviours</c> 배열만 돌면 된다. 씬 그래프가 끼지 않는 순수 관리 코드다.
    ///
    /// <b>캐시가 필요 없어졌다.</b> 간격 캐시는 탐색이 비싸서 있던 것이라, 새로 생긴 것이 그
    /// 간격만큼 늦게 떴다. 폴링마다 새로 읽으므로 그 지연이 사라진다.
    ///
    /// <b>옛 방식과의 대조는 걷어냈다.</b> Mirror 를 거치지 않고 씬에 올라오는 것이 있으면
    /// 놓치므로 한동안 옛 <c>FindObjectsByType</c> 를 10초마다 돌려 견줬다. 제보
    /// <c>09b4a20d</c>(75분 세션, 대조 939회: 상자 ~452·합성기 ~452·세피라이트 ~35)에서 셋 다
    /// <b>어긋남 0</b> 이었고, 그 대조가 한 번에 5.15ms 로 폴링 시간의 삼분의 일을 쓰고 있었다
    /// (최악 10.68ms - 60fps 한 프레임이 통째로 밀리는 값이다).
    ///
    /// <b>다시 열 조건.</b> 화면에 있어야 할 상자·세피라이트·합성기가 후보에 안 뜬다는 제보가
    /// 오면 그때 이 대조를 되살려 확인한다. 지운 코드는 <c>fa32859</c> 까지의 이력에 있다.
    /// </summary>
    internal static class NetworkRegistry
    {
        private static class Bucket<T> where T : NetworkBehaviour
        {
            internal static readonly List<T> Found = new List<T>();
        }

        /// <param name="includeInactive">
        /// 꺼져 있는 것까지 셀지. 세피라이트는 창이 열리는 동안 본체가 잠시 꺼지므로 켜야 한다.
        /// </param>
        /// <returns>다음 호출에 덮어써지는 목록. 폴링 안에서만 쓰고 들고 있지 않는다.</returns>
        public static List<T> All<T>(bool includeInactive = false) where T : NetworkBehaviour
        {
            var found = Bucket<T>.Found;
            found.Clear();

            // 호스트는 서버 쪽이 정본이고 참가자는 클라이언트 쪽만 있다.
            var spawned = NetworkServer.active ? NetworkServer.spawned : NetworkClient.spawned;
            if (spawned == null) return found;

            foreach (var pair in spawned)
            {
                var identity = pair.Value;
                if (identity == null) continue;
                if (!includeInactive && !identity.gameObject.activeInHierarchy) continue;

                var behaviours = identity.NetworkBehaviours;
                if (behaviours == null) continue;

                foreach (var behaviour in behaviours)
                {
                    if (behaviour is T match) found.Add(match);
                }
            }

            return found;
        }
    }
}
