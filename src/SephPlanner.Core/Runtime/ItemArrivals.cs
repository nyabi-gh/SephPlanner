using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 세션 도중 가방에 새로 들어와 아직 옮겨지지 않은 아이템을 가린다(<see cref="PlacedItem.Arrived"/>).
    ///
    /// 게임은 새 아이템을 보상 창에서 끌어 놓은 칸이나 첫 빈칸(<c>GridInventory.HasEmptySlot</c>)에 넣는다.
    /// 그 자리는 사용자가 고른 것이 아닐 수 있어, 한 번 옮겨진 뒤에야 사용자의 자리로 본다. 세션을 시작한
    /// 뒤 처음 본 가방은 언제 들어왔는지 모르므로 모두 이미 있던 것으로 친다.
    /// </summary>
    public sealed class ItemArrivals
    {
        private readonly HashSet<int> _settled = new HashSet<int>();
        private readonly Dictionary<int, GridPos> _arrivals = new Dictionary<int, GridPos>();
        private bool _started;

        public void Reset()
        {
            _started = false;
            _settled.Clear();
            _arrivals.Clear();
        }

        public void Mark(InventoryState? inventory)
        {
            if (inventory is null) return;

            // 번호 없는 아이템은 칸에서 만든 번호라 같은 아이템을 알아볼 수 없다.
            foreach (var item in inventory.Items) item.Arrived = !item.Immovable && Arrived(item);
            _started = true;
        }

        private bool Arrived(PlacedItem item)
        {
            if (!_started || _settled.Contains(item.InstanceId))
            {
                _settled.Add(item.InstanceId);
                return false;
            }
            if (!_arrivals.TryGetValue(item.InstanceId, out var cell))
                _arrivals[item.InstanceId] = cell = item.Position;
            if (cell == item.Position) return true;

            _arrivals.Remove(item.InstanceId);
            _settled.Add(item.InstanceId);
            return false;
        }
    }
}
