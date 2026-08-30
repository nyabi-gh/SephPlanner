using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>석판 조건 판정에 필요한 만큼의 배치 상태.</summary>
    public sealed class GridOccupancy
    {
        private readonly HashSet<GridPos> _items = new HashSet<GridPos>();
        private readonly HashSet<GridPos> _charms = new HashSet<GridPos>();

        public void AddItem(GridPos position, bool isCharm)
        {
            _items.Add(position);
            if (isCharm) _charms.Add(position);
        }

        public bool HasItem(GridPos position) => _items.Contains(position);
        public bool HasCharm(GridPos position) => _charms.Contains(position);
    }
}
