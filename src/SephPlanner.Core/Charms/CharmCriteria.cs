using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Charms
{
    /// <summary>
    /// 아티팩트가 스스로 요구하는 배치 조건. 게임의 <c>CharmActivateCriteria</c> 파생 클래스에 대응하며,
    /// 이름은 프리팹에서 읽은 타입 이름을 그대로 쓴다.
    ///
    /// 판정 로직은 각 클래스의 <c>GetCriteria</c>를 디컴파일해 그대로 옮긴 것이다. 아래에 보이는
    /// 5, 6, 7 같은 상수는 게임 코드 자체의 하드코딩이라(폭이 항상 6이라는 전제) grid.Width 로
    /// "고치면" 오히려 게임과 어긋난다. BothSidesAreEmpty 의 부분 행 판정도 게임과 동일하다.
    /// </summary>
    public enum CharmCriteriaKind
    {
        None,
        TopInInventory,
        BottomInInventory,
        SideEnd,
        Inside,
        Outlined,
        BothSideCharm,
        BothSidesAreEmpty,
        NeighborsAreFull,
        Near8MagicBook,
        /// <summary>배치와 무관한 전투 중 상태라 배치 탐색에서는 만족한 것으로 본다.</summary>
        FullHP,
    }

    public static class CharmCriteria
    {
        private static readonly (int X, int Y)[] Neighbors =
        {
            (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1),
        };

        public static CharmCriteriaKind FromTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return CharmCriteriaKind.None;

            const string prefix = "CharmActivateCriteria_";
            var name = typeName.StartsWith(prefix) ? typeName.Substring(prefix.Length) : typeName;

            foreach (CharmCriteriaKind kind in System.Enum.GetValues(typeof(CharmCriteriaKind)))
                if (kind.ToString() == name) return kind;

            return CharmCriteriaKind.None;
        }

        public static bool IsSatisfied(
            CharmCriteriaKind kind, GridPos pos, GridSpec grid, GridOccupancy occupancy)
        {
            switch (kind)
            {
                case CharmCriteriaKind.None:
                case CharmCriteriaKind.FullHP:
                    return true;

                case CharmCriteriaKind.TopInInventory:
                    return pos.Y == 0;

                case CharmCriteriaKind.SideEnd:
                    return pos.X == 0 || pos.X == 5;

                case CharmCriteriaKind.BottomInInventory:
                    return grid.ToIndex(pos.X, pos.Y) >= grid.Storage - 6;

                case CharmCriteriaKind.Inside:
                    if (pos.X <= 0 || pos.Y <= 0 || pos.X >= grid.Width - 1) return false;
                    return grid.ToIndex(pos.X, pos.Y) + 7 <= grid.Storage - 1;

                case CharmCriteriaKind.Outlined:
                    if (pos.X <= 0 || pos.Y <= 0 || pos.X >= grid.Width - 1) return true;
                    return grid.ToIndex(pos.X, pos.Y) >= grid.Storage - 6;

                case CharmCriteriaKind.BothSideCharm:
                    if (pos.X <= 0 || pos.X >= grid.Width - 1) return false;
                    return occupancy.HasCharm(pos.Offset(-1, 0)) && occupancy.HasCharm(pos.Offset(1, 0));

                case CharmCriteriaKind.BothSidesAreEmpty:
                {
                    if (pos.X <= 0 || pos.X >= grid.Width - 1) return false;
                    var remainder = grid.Storage % grid.Width;
                    var withinStorage = remainder == 0 || pos.Y < grid.Height - 1 || pos.X < remainder - 1;
                    return withinStorage
                           && !occupancy.HasItem(pos.Offset(-1, 0))
                           && !occupancy.HasItem(pos.Offset(1, 0));
                }

                case CharmCriteriaKind.NeighborsAreFull:
                    foreach (var (dx, dy) in Neighbors)
                        if (!occupancy.HasItem(pos.Offset(dx, dy))) return false;
                    return true;

                case CharmCriteriaKind.Near8MagicBook:
                    foreach (var (dx, dy) in Neighbors)
                        if (occupancy.HasMagicCharm(pos.Offset(dx, dy))) return true;
                    return false;

                default:
                    return true;
            }
        }
    }
}
