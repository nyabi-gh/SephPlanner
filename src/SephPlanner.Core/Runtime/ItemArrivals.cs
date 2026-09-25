using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 세션 도중 가방에 새로 들어와 아직 옮겨지지 않은 아이템을 가린다(<see cref="PlacedItem.Arrived"/>).
    ///
    /// 게임은 새 아이템을 보상 창에서 끌어 놓은 칸이나 첫 빈칸(<c>GridInventory.HasEmptySlot</c>)에 넣는다.
    /// 그 자리는 사용자가 고른 것이 아닐 수 있어, 한 번 옮겨진 뒤에야 사용자의 자리로 본다. 세션을 시작한
    /// 뒤 처음 본 가방은 언제 들어왔는지 모르므로 모두 이미 있던 것으로 친다. 다만 빈 가방은 기준으로
    /// 삼지 않는다 - 저장된 판은 아바타가 빈 가방으로 생긴 뒤에 서버가 아이템을 채우므로
    /// (<c>PlayerSpawner.Initialize</c>), 그 틈에 기준을 잡으면 복원된 것이 전부 새로 들어온 것이 된다.
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

        /// <summary>
        /// <paramref name="tracked"/> 가 참인 아이템에만 표시한다. 표시는 계획 지문에 들어가므로, 쓰지 않는
        /// 아이템까지 표시하면 줍기만 해도 지문이 흔들린다.
        /// </summary>
        public void Mark(InventoryState? inventory, Func<PlacedItem, bool> tracked)
        {
            if (inventory is null) return;

            if (!_started)
            {
                if (inventory.Items.Count == 0 && inventory.Tablets.Count == 0) return;
                foreach (var item in inventory.Items)
                {
                    _settled.Add(item.InstanceId);
                    item.Arrived = false;
                }
                _started = true;
                return;
            }

            // 번호 없는 아이템은 칸에서 만든 번호라 같은 아이템을 알아볼 수 없다.
            foreach (var item in inventory.Items)
                item.Arrived = !item.Immovable && tracked(item) && Arrived(item);
        }

        private bool Arrived(PlacedItem item)
        {
            if (_settled.Contains(item.InstanceId)) return false;
            if (!_arrivals.TryGetValue(item.InstanceId, out var cell))
                _arrivals[item.InstanceId] = cell = item.Position;
            if (cell == item.Position) return true;

            _arrivals.Remove(item.InstanceId);
            _settled.Add(item.InstanceId);
            return false;
        }
    }
}
