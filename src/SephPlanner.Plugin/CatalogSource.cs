using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임 리소스에서 읽은 정의를 메모리에 한 번 만들어 둔다.
    ///
    /// <see cref="CatalogDump"/>는 같은 데이터를 파일로 떠서 오버레이에 넘기는 쪽이고, 이쪽은
    /// 게임 안에서 직접 배치를 풀 때 쓴다. 리소스 접근은 유니티 스레드에서만 되므로 만드는
    /// 시점이 메인 스레드여야 하고, 그래서 백그라운드 솔버에 넘기기 전에 미리 지어 둔다.
    /// </summary>
    internal static class CatalogSource
    {
        private static ICatalog _catalog;

        public static ICatalog Get()
        {
            if (_catalog != null) return _catalog;

            var tablets = ItemCatalog.LoadTablets();
            var charms = ItemCatalog.LoadCharms();
            var combos = ItemCatalog.LoadCombos();

            // 아티팩트가 레벨마다 주는 값어치를 여기서도 실어 준다. 이것이 없으면 점수가 레어도
            // 어림값으로 물러서서, 오버레이가 말하는 점수와 인게임 패널이 말하는 점수가 달라진다.
            CharmStatWorth.Apply(charms, ItemCatalog.LoadStatMeasurement());

            return _catalog = new Catalog(tablets, charms, combos);
        }

        /// <summary>F9 로 다시 덤프할 때처럼 정의가 바뀌었을 수 있으면 버린다.</summary>
        public static void Invalidate() => _catalog = null;
    }
}
