using System.Collections.Generic;
using Mirror;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 오버레이가 보낸 최종 배치를 게임에 적용한다. 게임이 스스로 쓰는 경로만 탄다.
    /// 자리는 수동 드래그와 같은 <c>GridInventory.Swap</c>, 회전은 게임 내장 자동 정리
    /// (<c>AutoArrangeInventoryForBestCharmLevels</c>)와 같은 <c>Permission</c> 스코프 안의
    /// <c>Networkrotation</c> 설정이다.
    ///
    /// 멀티 세션에서는 실행하지 않는다. 클라이언트 쓰기의 동기화가 검증되지 않았기 때문이다
    /// (docs/LEGAL.md). 싱글은 호스트 모드라 서버 API를 그대로 쓸 수 있다.
    /// </summary>
    internal static class PlanApplier
    {
        /// <summary>적용 결과를 사람이 읽을 한 줄로 돌려준다. 실패면 아무것도 바꾸지 않은 상태다.</summary>
        public static string Apply(ApplyPlanCommand command)
        {
            if (GameReader.IsMultiplayerSession())
                return "멀티플레이 세션에서는 자동 배치를 실행하지 않습니다.";
            if (!NetworkServer.active)
                return "서버가 활성 상태가 아니라 자동 배치를 실행할 수 없습니다.";

            var avatar = GameReader.FindLocalPlayer();
            var inventory = avatar != null ? avatar.Inventory : null;
            if (inventory == null || avatar.IsDead)
                return "적용할 인벤토리가 없습니다.";

            // 명령이 만들어진 뒤 상태가 바뀌었을 수 있다. 하나라도 어긋나면 통째로 물러선다.
            var error = Validate(command, inventory, out var positions, out var occupants);
            if (error != null) return error;

            var swaps = ApplySwaps(command, inventory, positions, occupants);
            var rotations = ApplyRotations(command, inventory, out var lockedTablets);

            var report = $"자동 배치 완료 - 이동 {swaps}건, 회전 {rotations}건";
            if (lockedTablets > 0) report += $", 회전 잠긴 석판 {lockedTablets}개는 건너뜀";
            return report;
        }

        private static string Validate(
            ApplyPlanCommand command, GridInventory inventory,
            out Dictionary<int, GridPos> positions, out Dictionary<GridPos, int> occupants)
        {
            positions = new Dictionary<int, GridPos>();
            occupants = new Dictionary<GridPos, int>();

            var cellCounts = new Dictionary<int, int>();
            foreach (var pair in inventory.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null || !IsOnGrid(pair.Key.x, pair.Key.y, inventory)) continue;

                cellCounts[instance.InstanceID] =
                    cellCounts.TryGetValue(instance.InstanceID, out var count) ? count + 1 : 1;
                positions[instance.InstanceID] = new GridPos(pair.Key.x, pair.Key.y);
                occupants[new GridPos(pair.Key.x, pair.Key.y)] = instance.InstanceID;
            }

            // 여러 칸을 차지하는 아이템은 한 칸짜리 맞바꿈으로 옮기면 망가진다. 모델에 없는
            // 종류이므로 하나라도 있으면 손대지 않는다.
            foreach (var count in cellCounts)
            {
                if (count.Value > 1)
                    return $"여러 칸을 차지하는 아이템(인스턴스 {count.Key})이 있어 자동 배치를 중단합니다.";
            }

            var destinations = new HashSet<GridPos>();
            foreach (var target in command.Targets)
            {
                if (!positions.ContainsKey(target.InstanceId))
                    return "배치 계산 이후 인벤토리가 바뀌어 자동 배치를 중단합니다. 잠시 뒤 다시 시도하세요.";
                if (!IsOnGrid(target.To.X, target.To.Y, inventory))
                    return $"목표 칸 {target.To}이 격자 밖이라 자동 배치를 중단합니다.";
                if (!destinations.Add(target.To))
                    return $"목표 칸 {target.To}이 겹쳐 자동 배치를 중단합니다.";
            }
            return null;
        }

        private static int ApplySwaps(
            ApplyPlanCommand command, GridInventory inventory,
            Dictionary<int, GridPos> positions, Dictionary<GridPos, int> occupants)
        {
            var swaps = 0;
            foreach (var target in command.Targets)
            {
                var from = positions[target.InstanceId];
                var to = target.To;
                if (from == to) continue;

                inventory.Swap((sbyte)from.X, (sbyte)from.Y, (sbyte)to.X, (sbyte)to.Y);
                swaps++;

                if (occupants.TryGetValue(to, out var displaced))
                {
                    occupants[from] = displaced;
                    positions[displaced] = from;
                }
                else
                {
                    occupants.Remove(from);
                }
                occupants[to] = target.InstanceId;
                positions[target.InstanceId] = to;
            }
            return swaps;
        }

        private static int ApplyRotations(
            ApplyPlanCommand command, GridInventory inventory, out int lockedTablets)
        {
            lockedTablets = 0;

            var pending = new List<KeyValuePair<StoneTablet, int>>();
            foreach (var target in command.Targets)
            {
                if (!target.IsTablet) continue;

                var tablet = FindTablet(inventory, target.InstanceId);
                if (tablet == null || tablet.rotation == target.Rotation) continue;

                // 저주 등으로 인스턴스 단위로 회전이 잠길 수 있다. 솔버는 아직 이를 모른다.
                if (!DungeonManager.IsTabletRotatable(tablet.instanceID, tablet.isRotatable))
                {
                    lockedTablets++;
                    continue;
                }
                pending.Add(new KeyValuePair<StoneTablet, int>(tablet, target.Rotation));
            }
            if (pending.Count == 0) return 0;

            // Permission 이 닫힐 때 레벨 행렬이 다시 계산된다. 회전을 모아 한 번에 처리한다.
            using (new GridInventory.Permission(inventory))
            {
                foreach (var pair in pending)
                    pair.Key.Networkrotation = pair.Value;
            }
            return pending.Count;
        }

        private static StoneTablet FindTablet(GridInventory inventory, int instanceId)
        {
            foreach (var pair in inventory.stoneTablets)
            {
                if (pair.Value != null && pair.Value.instanceID == instanceId) return pair.Value;
            }
            return null;
        }

        private static bool IsOnGrid(int x, int y, GridInventory inventory) =>
            x >= 0 && x < inventory.Width &&
            y >= 0 && y < inventory.Height &&
            y * inventory.Width + x < inventory.CurrentInventoryStorage;
    }
}
