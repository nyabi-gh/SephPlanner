using System.Collections.Generic;
using Mirror;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 지금 씬에 있는 <see cref="GridInventory"/> 전부.
    ///
    /// <b>왜 씬 전수 탐색을 그만두었나.</b> 계측이 답을 줬다(제보 <c>6961d1a0</c>) - 상자·상점
    /// 찾기 2,697ms 가운데 <c>FindObjectsByType</c> 한 줄이 2,671ms(99%)였고, 들고 있는 목록을
    /// 훑는 것과 거리·보임 판정은 합쳐서 24ms 였다. 계획서가 1순위로 꼽았던 훑기는 0.8ms 로
    /// 사실상 0 이다. 즉 고칠 것은 <b>전수 탐색 그 자체</b> 하나였다.
    ///
    /// <b>그럴 필요가 없었다.</b> <c>GridInventory</c> 는 <c>NetworkBehaviour</c> 라 Mirror 가
    /// 이미 전부 등록해 두고 있다 - <c>spawned</c> 는 번호 → <c>NetworkIdentity</c> 사전이고,
    /// 각 아이덴티티는 제 <c>NetworkBehaviours</c> 를 배열로 캐시해 둔다. 씬 그래프를 걷는 대신
    /// 그 둘을 도는 것은 순수 관리 코드다.
    ///
    /// <b>덤으로 캐시가 필요 없어졌다.</b> 1초 간격 캐시는 전수 탐색이 비싸서 있던 것이라,
    /// 새로 떨어진 꾸러미가 최대 1초 늦게 떴다. 이제 폴링마다 새로 보므로 그 지연이 사라진다.
    /// </summary>
    internal static class NetworkedInventories
    {
        private static readonly List<GridInventory> Found = new List<GridInventory>();

        /// <summary>
        /// 돌려주는 목록은 다음 호출에 덮어써진다. 폴링 안에서만 쓰고 들고 있지 않는다.
        /// </summary>
        public static List<GridInventory> All()
        {
            Found.Clear();

            // 호스트는 서버 쪽이 정본이다. 참가자는 클라이언트 쪽만 있다.
            var spawned = NetworkServer.active ? NetworkServer.spawned : NetworkClient.spawned;
            if (spawned == null) return Found;

            foreach (var pair in spawned)
            {
                var identity = pair.Value;
                if (identity == null) continue;

                // 전수 탐색은 비활성 오브젝트를 빼고 찾았다. 등록부에는 남아 있으므로 여기서 뺀다 -
                // 풀에 들어간 꾸러미가 그것이다.
                if (!identity.gameObject.activeInHierarchy) continue;

                var behaviours = identity.NetworkBehaviours;
                if (behaviours == null) continue;

                foreach (var behaviour in behaviours)
                {
                    if (behaviour is GridInventory inventory) Found.Add(inventory);
                }
            }
            return Found;
        }
    }
}
