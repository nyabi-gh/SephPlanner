using System;
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
        /// <summary>
        /// 적용 결과를 사람이 읽을 한 줄로 돌려준다. 사전 검증에서 물러서면 아무것도 바꾸지 않은
        /// 상태고, 적용 도중 실패하면 이미 실행한 걸음을 역순으로 되돌린다. 되돌리기까지 실패한
        /// 경우에만 중간 상태가 남으며 그 사실이 결과에 그대로 적힌다.
        /// </summary>
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

            var journal = new List<KeyValuePair<GridPos, GridPos>>();
            var swaps = ApplySwaps(command, inventory, positions, occupants, journal, out var failure);
            if (failure != null) return failure;

            var rotations = ApplyRotations(command, inventory, out var lockedTablets, out failure);
            if (failure != null)
            {
                // 옮겨졌지만 안 돌아간 배치는 솔버가 평가한 적 없는 상태다. 이동도 함께 되돌린다.
                return failure + " " + Rollback(inventory, journal);
            }

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

            // 칸 수는 격자 안팎을 가리지 않고 센다. 열린 격자 경계에 걸친 아이템을 격자 안 칸만으로
            // 세면 한 칸짜리로 보여 가드를 빠져나가고, 한 칸짜리 맞바꿈이 그 아이템을 찢는다.
            var cellCounts = new Dictionary<int, int>();
            var touchesGrid = new HashSet<int>();
            foreach (var pair in inventory.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null) continue;

                cellCounts[instance.InstanceID] =
                    cellCounts.TryGetValue(instance.InstanceID, out var count) ? count + 1 : 1;
                if (!IsOnGrid(pair.Key.x, pair.Key.y, inventory)) continue;

                touchesGrid.Add(instance.InstanceID);
                positions[instance.InstanceID] = new GridPos(pair.Key.x, pair.Key.y);
                occupants[new GridPos(pair.Key.x, pair.Key.y)] = instance.InstanceID;
            }

            // 여러 칸을 차지하는 아이템은 한 칸짜리 맞바꿈으로 옮기면 망가진다. 모델에 없는
            // 종류이므로 격자에 걸쳐 있는 것이 하나라도 있으면 손대지 않는다.
            foreach (var count in cellCounts)
            {
                if (count.Value > 1 && touchesGrid.Contains(count.Key))
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
            Dictionary<int, GridPos> positions, Dictionary<GridPos, int> occupants,
            List<KeyValuePair<GridPos, GridPos>> journal, out string failure)
        {
            failure = null;
            var swaps = 0;
            foreach (var target in command.Targets)
            {
                var from = positions[target.InstanceId];
                var to = target.To;
                if (from == to) continue;

                // 적용을 시작한 뒤에 동료가 접속했을 수 있다. 멀티가 된 순간 더 진행하지 않는다.
                if (GameReader.IsMultiplayerSession())
                {
                    failure = "적용 도중 멀티플레이 세션이 되어 자동 배치를 중단했습니다. " +
                              Rollback(inventory, journal);
                    return swaps;
                }

                try
                {
                    inventory.Swap((sbyte)from.X, (sbyte)from.Y, (sbyte)to.X, (sbyte)to.Y);
                }
                catch (Exception ex)
                {
                    failure = $"이동 중 오류({ex.Message})가 나 자동 배치를 중단했습니다. " +
                              Rollback(inventory, journal);
                    return swaps;
                }
                journal.Add(new KeyValuePair<GridPos, GridPos>(from, to));
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

        /// <summary>
        /// 실행한 맞바꿈을 역순으로 재생해 원래 배치로 되돌린다. Swap 은 자기 자신이 역연산이라
        /// 이전 상태를 따로 저장할 필요가 없다.
        /// </summary>
        private static string Rollback(GridInventory inventory, List<KeyValuePair<GridPos, GridPos>> journal)
        {
            for (var i = journal.Count - 1; i >= 0; i--)
            {
                var from = journal[i].Key;
                var to = journal[i].Value;
                try
                {
                    inventory.Swap((sbyte)to.X, (sbyte)to.Y, (sbyte)from.X, (sbyte)from.Y);
                }
                catch (Exception ex)
                {
                    return $"되돌리기도 실패해 인벤토리가 중간 상태로 남았습니다({ex.Message}). " +
                           "손으로 정리한 뒤 다시 시도하세요.";
                }
            }
            return "원래 배치로 되돌렸습니다.";
        }

        private static int ApplyRotations(
            ApplyPlanCommand command, GridInventory inventory, out int lockedTablets, out string failure)
        {
            lockedTablets = 0;
            failure = null;

            var pending = new List<KeyValuePair<StoneTablet, int>>();
            try
            {
                foreach (var target in command.Targets)
                {
                    if (!target.IsTablet) continue;

                    var tablet = FindTablet(inventory, target.InstanceId);
                    if (tablet == null || tablet.rotation == target.Rotation) continue;

                    // 솔버도 스냅샷의 인스턴스별 회전 가능 여부를 보지만, 계산 이후 저주 등으로
                    // 잠겼을 수 있어 적용 직전에 한 번 더 확인한다.
                    if (!DungeonManager.IsTabletRotatable(tablet.instanceID, tablet.isRotatable))
                    {
                        lockedTablets++;
                        continue;
                    }
                    pending.Add(new KeyValuePair<StoneTablet, int>(tablet, target.Rotation));
                }
            }
            catch (Exception ex)
            {
                // 아직 아무것도 쓰지 않았다. 회전만 포기하면 이동을 되돌릴지 호출자가 정한다.
                failure = $"회전 준비 중 오류가 났습니다({ex.Message}).";
                return 0;
            }
            if (pending.Count == 0) return 0;

            // Permission 이 닫힐 때 레벨 행렬이 다시 계산된다. 회전을 모아 한 번에 처리한다.
            var original = new List<KeyValuePair<StoneTablet, int>>();
            try
            {
                using (new GridInventory.Permission(inventory))
                {
                    foreach (var pair in pending)
                    {
                        original.Add(new KeyValuePair<StoneTablet, int>(pair.Key, pair.Key.rotation));
                        pair.Key.Networkrotation = pair.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                // 일부만 돌아간 채 남지 않게 이미 돌린 것을 원래 각도로 되돌린다.
                try
                {
                    using (new GridInventory.Permission(inventory))
                    {
                        foreach (var pair in original) pair.Key.Networkrotation = pair.Value;
                    }
                    failure = $"회전 중 오류가 나 원래 각도로 되돌렸습니다({ex.Message}).";
                }
                catch (Exception restore)
                {
                    failure = $"회전 중 오류가 났고 되돌리기도 실패했습니다({ex.Message} / {restore.Message}).";
                }
                return 0;
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
