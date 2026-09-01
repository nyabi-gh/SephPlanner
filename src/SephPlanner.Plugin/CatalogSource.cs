using System;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임 리소스에서 읽은 정의를 메모리에 한 번 만들어 둔다.
    ///
    /// <see cref="CatalogDump"/>는 같은 데이터를 진단 파일로 저장하고, 이쪽은 게임 안에서 직접
    /// 배치를 풀 때 쓴다. 리소스 접근은 유니티 스레드에서만 되므로 만드는
    /// 시점이 메인 스레드여야 하고, 그래서 백그라운드 솔버에 넘기기 전에 미리 지어 둔다.
    /// </summary>
    internal static class CatalogSource
    {
        private static ICatalog _catalog;

        /// <summary>마지막으로 짓지 못한 이유. 지어졌으면 빈 문자열이다.</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>
        /// 아직 지을 수 없으면 <c>null</c> 을 돌려준다. 부르는 쪽은 다음 기회에 다시 물어보면 된다.
        /// </summary>
        public static ICatalog Get()
        {
            if (_catalog != null) return _catalog;

            try
            {
                var tablets = ItemCatalog.LoadTablets();
                var charms = ItemCatalog.LoadCharms();
                var combos = ItemCatalog.LoadCombos();

                // 아티팩트가 레벨마다 주는 값어치를 실어 레어도 어림값으로 물러서지 않게 한다.
                CharmStatWorth.Apply(charms, ItemCatalog.LoadStatMeasurement());

                LastError = "";
                return _catalog = new Catalog(tablets, charms, combos);
            }
            catch (Exception ex)
            {
                // 부팅 직후에는 게임의 LocalizationManager 가 아직 표를 안 들고 있어서 이름을
                // 물어보면 터진다(BepInEx 로그에 NullReferenceException 으로 남았다). 반쯤 지은
                // 것을 캐시하면 이름이 영영 빈 채로 굳으므로, 짓지 않고 물러서서 다음에 다시 짓는다.
                // 조용히 넘기지는 않는다 - LastError 를 플러그인이 한 번 로그로 남긴다.
                LastError = ex.Message;
                return null;
            }
        }

        /// <summary>F9 로 다시 덤프할 때처럼 정의가 바뀌었을 수 있으면 버린다.</summary>
        public static void Invalidate()
        {
            _catalog = null;
            LastError = "";
        }
    }
}
