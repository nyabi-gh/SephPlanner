using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 씬 전수 탐색의 결과를 잠시 들고 있는다.
    ///
    /// <b>왜 필요한가.</b> 실기에서 재어 보니 <c>FindObjectsByType</c> 한 번이 이 게임에서
    /// 4ms(비활성 포함은 7ms)다. 폴링마다 셋을 부르면 15ms이고, 초당 네 번이면 프레임 예산을
    /// 확정적으로 넘긴다. 안쪽을 깎을 수 있는 것이 아니므로 <b>덜 부르는 것</b>만이 답이다.
    ///
    /// <b>늦어지지 않는 이유.</b> 비싼 것은 <em>찾는</em> 일이지 <em>보는</em> 일이 아니다.
    /// 상자가 열리거나 세피라이트가 채워지는 것은 이미 씬에 있던 오브젝트의 상태가 바뀌는
    /// 것이라, 목록을 들고 있어도 매 폴링 그 상태를 새로 읽으면 지연이 없다. 늦어지는 것은
    /// <b>새로 생긴 오브젝트</b>뿐이고, 그것은 간격만큼이다.
    ///
    /// 층이 바뀌면 오브젝트가 파괴되므로, 하나라도 죽어 있으면 간격과 무관하게 곧바로 다시 찾는다.
    /// </summary>
    internal sealed class SceneCache<T> where T : Object
    {
        private readonly float _interval;
        private readonly FindObjectsInactive _inactive;

        private T[] _found = new T[0];
        private float _refreshedAt = float.NegativeInfinity;

        /// <param name="interval">다시 찾기까지 최소로 기다릴 시간(초).</param>
        /// <param name="inactive">비활성 오브젝트까지 찾을지. 켜면 눈에 띄게 비싸진다.</param>
        public SceneCache(float interval, FindObjectsInactive inactive = FindObjectsInactive.Exclude)
        {
            _interval = interval;
            _inactive = inactive;
        }

        public T[] Get()
        {
            if (Time.unscaledTime - _refreshedAt < _interval && Alive()) return _found;

            _refreshedAt = Time.unscaledTime;
            _found = Object.FindObjectsByType<T>(_inactive, FindObjectsSortMode.None);
            return _found;
        }

        /// <summary>
        /// 들고 있던 것이 아직 살아 있는가. 하나라도 파괴됐으면 층이 바뀐 것이므로 목록 전체를
        /// 믿을 수 없다. 유니티가 덮어쓴 <c>==</c> 라야 파괴된 객체를 null 로 본다.
        /// </summary>
        private bool Alive()
        {
            foreach (var item in _found)
            {
                if (item == null) return false;
            }
            return true;
        }
    }
}
