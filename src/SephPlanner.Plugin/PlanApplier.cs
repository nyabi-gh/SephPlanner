using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 솔버가 만든 최종 배치를 게임에 적용한다. 게임이 스스로 쓰는 경로만 탄다.
    ///
    /// <b>호스트(싱글 포함)</b>에서는 쓰기가 그 자리에서 끝난다. 자리는 수동 드래그와 같은
    /// <c>GridInventory.Swap</c>, 회전은 게임 내장 자동 정리와 같은 <c>Permission</c> 스코프 안의
    /// <c>Networkrotation</c> 설정이다.
    ///
    /// <b>참가자로 접속한 세션</b>에서는 같은 경로가 서버로 가는 Command 가 된다 - <c>Swap</c> 은
    /// <c>CmdSwap</c>, 회전은 우클릭이 타는 <c>DoClickAction</c>(→ <c>CmdDoClickAction</c> →
    /// <c>StoneTablet.Rotate</c>)이다. 왕복이라 부른 즉시 반영되지 않으므로 걸음마다 동기화를
    /// 기다린다. 이 적용기가 코루틴인 이유가 그것이다.
    ///
    /// 걸음 순서·저널·되돌리기·시간 상한은 <see cref="ApplyPlanRoutine"/>(Core)에 있고 게임 없이
    /// 시험된다. 여기 남는 것은 쓰기 전의 사전 검증과, 게임 타입을 <see cref="IInventoryPort"/>
    /// 로 감싸는 것뿐이다.
    ///
    /// 멀티 세션은 기본으로 잠그고, 함께 플레이하는 사람의 동의를 받은 뒤 설정에서 연다.
    /// </summary>
    internal static class PlanApplier
    {
        private static bool _running;
        private static ApplyPlanRoutine _routine;
        private static GridInventory _uncertainInventory;

        public static bool RecoveryRequired => _uncertainInventory != null;
        public const string RecoveryMessage =
            "이전 명령의 서버 반영이 불확실해 자동 배치를 잠갔습니다. 방에 재접속한 뒤 시도하세요.";

        /// <summary>
        /// 적용이 진행 중인가. 클라이언트에서는 여러 프레임에 걸치므로, 겹쳐 시작하면 두 적용기가
        /// 같은 격자를 서로 다른 예상 위에서 밀게 된다.
        ///
        /// 시한을 함께 보는 것은 유니티가 코루틴을 소유한 오브젝트가 비활성화될 때 반복자를
        /// <c>Dispose</c> 하지 않기 때문이다. 그러면 <c>finally</c> 가 돌지 않아 F8 이 세션 내내
        /// "진행 중"으로 잠긴다.
        /// </summary>
        public static bool InProgress =>
            _running && (_routine == null || Time.unscaledTime < _routine.AbandonAt);

        /// <summary>진행 상황 한 줄. 참가자 세션의 적용은 수 초가 걸리므로 화면에 이것이라도 있어야 한다.</summary>
        public static string Progress => _routine?.Progress ?? "";

        /// <summary>
        /// 배치를 적용하고 사람이 읽을 한 줄을 <paramref name="report"/> 로 돌려준다. 사전 검증에서
        /// 물러서면 아무것도 바꾸지 않은 상태다. 적용 도중 실패하면 확인된 걸음만 안전하게 되돌린다.
        /// 미확정 쓰기나 외부 변경 때문에 복구하지 못하면 중간 상태를 알리고 필요한 경우 재접속을 안내한다.
        /// </summary>
        /// <param name="report">
        /// 사람이 읽을 한 줄과, 계획대로 다 놓였는지. 두 번째가 참일 때만 지금 가방이 곧 그
        /// 계획이므로, 부르는 쪽이 그것으로 다음 재계산을 건너뛴다.
        /// </param>
        public static IEnumerator Apply(
            ApplyPlanCommand command, bool allowMultiplayer, Action<string, bool> report, Action<string> diagnostic = null)
        {
            if (RecoveryRequired)
            {
                report(RecoveryMessage, false);
                yield break;
            }
            if (InProgress)
            {
                report("이전 자동 배치가 아직 끝나지 않았습니다. 잠시 뒤 다시 누르세요.", false);
                yield break;
            }

            _running = true;
            GridInventory inventory = null;
            try
            {
                var setup = Prepare(command, allowMultiplayer);
                if (setup.Failure != null)
                {
                    if (setup.DiagnosticError != null) diagnostic?.Invoke(setup.DiagnosticError);
                    report(setup.Failure, false);
                    yield break;
                }
                inventory = setup.Inventory;

                // 안쪽 반복자는 유니티에 넘기지 않고 직접 돌린다. 넘기면 기다릴 것이 없어도
                // 단계마다 프레임을 쓰는데, 호스트의 적용은 키를 누른 그 프레임에 통째로 끝나야
                // 한다 - 중간에 프레임이 끼면 그 사이에 게임 상태가 바뀔 수 있고, 그러면
                // 원자적이라는 전제가 무너진다.
                var port = new GridInventoryPort(setup.Inventory);
                _routine = new ApplyPlanRoutine(
                    command, port, () => Time.unscaledTime, allowMultiplayer);
                var run = _routine.Run();
                while (run.MoveNext())
                {
                    // 코루틴이 강제로 중단돼 finally가 실행되지 않아도 미확정 쓰기를 잊지 않는다.
                    _uncertainInventory = _routine.RequiresResync ? inventory : null;
                    yield return run.Current;
                }
                var detail = port.DiagnosticError ?? _routine.DiagnosticError;
                if (detail != null) diagnostic?.Invoke(detail);
                report(_routine.Result, _routine.Settled);
            }
            finally
            {
                if (_routine != null)
                    _uncertainInventory = _routine.RequiresResync ? inventory : null;
                _running = false;
                _routine = null;
            }
        }

        private sealed class Preparation
        {
            public string Failure;
            public string DiagnosticError;
            public GridInventory Inventory;
        }

        /// <summary>
        /// 쓰기 전에 물러설 이유를 전부 본다. 여기서 실패하면 아무것도 바꾸지 않은 상태다.
        /// 프레임을 넘기지 않으므로 예외도 여기서 잡는다 - 코루틴 본문에서는 잡을 수 없다.
        /// </summary>
        private static Preparation Prepare(ApplyPlanCommand command, bool allowMultiplayer)
        {
            try
            {
                if (!CatalogDump.QueryVerificationPassed())
                    return Denied("석판 효과 계산을 검증하지 못해 자동 배치를 실행하지 않습니다. ‘아이템 데이터 다시 읽기’(기본 F9)를 실행하세요.");
                if (command.ExpectedCatalogGeneration.Length == 0 ||
                    command.ExpectedCatalogGeneration != CatalogDump.ActiveGeneration)
                    return Denied("계획을 만든 카탈로그가 더 이상 최신이 아니어서 자동 배치를 실행하지 않습니다.");
                if (command.ExpectedPlacementFingerprint.Length == 0 ||
                    command.ExpectedPlanningContextFingerprint.Length == 0)
                    return Denied("계획의 상태 지문이 없어 자동 배치를 실행하지 않습니다.");

                if (GameReader.IsMultiplayerSession() && !allowMultiplayer)
                    return Denied("멀티플레이 세션에서는 자동 배치를 실행하지 않습니다. 설정에서 열 수 있습니다.");

                // 호스트면 서버 로컬에서 끝나고, 참가자면 Command 로 서버에 간다. 둘 다 아니면
                // 쓰기가 나갈 곳이 없다.
                if (!NetworkServer.active && !NetworkClient.active)
                    return Denied("네트워크 세션이 없어 자동 배치를 실행할 수 없습니다.");

                var avatar = GameReader.FindLocalPlayer();
                var inventory = avatar != null ? avatar.Inventory : null;
                if (inventory == null || avatar.IsDead)
                    return Denied("적용할 인벤토리가 없습니다.");

                var liveSnapshot = GameReader.Read(0, includeRecommendations: false);
                var livePlacementFingerprint = PlanFingerprint.Placement(
                    liveSnapshot, command.ExpectedPlanningContextFingerprint);
                var liveWeaponId = liveSnapshot.Run?.WeaponId ?? "";

                // 명령이 만들어진 뒤 상태가 바뀌었을 수 있다. 하나라도 어긋나면 통째로 물러선다.
                var error = Validate(command, inventory, livePlacementFingerprint, liveWeaponId);
                if (error != null) return Denied(error);

                return new Preparation { Inventory = inventory };
            }
            catch (Exception ex)
            {
                return new Preparation
                {
                    Failure = $"자동 배치를 준비하다 오류가 나 아무것도 바꾸지 않았습니다({ex.Message}).",
                    DiagnosticError = ex.ToString(),
                };
            }
        }

        private static Preparation Denied(string reason) => new Preparation { Failure = reason };

        private static string Validate(
            ApplyPlanCommand command, GridInventory inventory,
            string livePlacementFingerprint, string liveWeaponId)
        {
            var positions = new Dictionary<int, GridPos>();

            // 칸 수는 격자 안팎을 가리지 않고 센다. 열린 격자 경계에 걸친 아이템을 격자 안 칸만으로
            // 세면 한 칸짜리로 보여 가드를 빠져나가고, 한 칸짜리 맞바꿈이 그 아이템을 찢는다.
            var grid = GameReader.GridOf(inventory);
            var cellCounts = new Dictionary<int, int>();
            var touchesGrid = new HashSet<int>();
            foreach (var pair in inventory.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null) continue;

                // 번호 없는 아이템은 칸에서 만든 번호로 가리킨다(ItemIdentity). 계획도 같은
                // 번호를 쓰므로, 여기서 0 그대로 두면 목표를 못 찾아 자동 배치가 거절된다.
                var inGrid = grid.Contains(pair.Key.x, pair.Key.y);
                var identity = inGrid
                    ? ItemIdentity.Of(instance.InstanceID, grid, pair.Key.x, pair.Key.y)
                    : instance.InstanceID;

                cellCounts[identity] = cellCounts.TryGetValue(identity, out var count) ? count + 1 : 1;
                if (!inGrid) continue;

                touchesGrid.Add(identity);
                positions[identity] = new GridPos(pair.Key.x, pair.Key.y);
            }

            // 여러 칸을 차지하는 아이템은 한 칸짜리 맞바꿈으로 옮기면 망가진다. 모델에 없는
            // 종류이므로 격자에 걸쳐 있는 것이 하나라도 있으면 손대지 않는다.
            foreach (var count in cellCounts)
            {
                if (count.Value > 1 && touchesGrid.Contains(count.Key))
                    return $"여러 칸을 차지하는 아이템(인스턴스 {count.Key})이 있어 자동 배치를 중단합니다.";
            }

            var tablets = new Dictionary<int, StoneTablet>();
            foreach (var pair in inventory.stoneTablets)
            {
                var tablet = pair.Value;
                if (tablet != null) tablets[tablet.instanceID] = tablet;
            }

            var liveItems = new List<LivePlanItem>();
            foreach (var pair in positions)
            {
                tablets.TryGetValue(pair.Key, out var tablet);
                liveItems.Add(new LivePlanItem
                {
                    InstanceId = pair.Key,
                    Position = pair.Value,
                    IsTablet = tablet != null,
                    Rotation = tablet != null ? tablet.rotation : 0,
                    CanRotate = tablet != null &&
                                DungeonManager.IsTabletRotatable(tablet.instanceID, tablet.isRotatable),
                });
            }

            var stateError = ApplyPlanValidator.Validate(
                command, liveItems, inventory.Width, inventory.Height, inventory.CurrentInventoryStorage,
                livePlacementFingerprint, liveWeaponId);
            if (stateError != null) return stateError;

            foreach (var target in command.Targets)
            {
                if (!grid.Contains(target.To))
                    return $"목표 칸 {target.To}이 격자 밖이라 자동 배치를 중단합니다.";
            }
            return null;
        }

        /// <summary>
        /// 게임의 <c>GridInventory</c> 를 적용기가 보는 모양으로 감싼다. 쓰기는 예외를 이유 문자열로
        /// 바꿔 돌려준다 - 반복자 안에서는 잡을 수 없다.
        /// </summary>
        private sealed class GridInventoryPort : IInventoryPort
        {
            private readonly GridInventory _inventory;
            public string DiagnosticError { get; private set; }

            public GridInventoryPort(GridInventory inventory)
            {
                _inventory = inventory;
            }

            public bool Alive => _inventory != null;
            public bool WritesLandImmediately => NetworkServer.active;
            public bool IsMultiplayerSession => GameReader.IsMultiplayerSession();

            public int InstanceAt(GridPos cell)
            {
                foreach (var pair in _inventory.inventoryMatrix)
                {
                    if (pair.Key.x == cell.X && pair.Key.y == cell.Y)
                        return pair.Value != null ? pair.Value.InstanceID : 0;
                }
                return 0;
            }

            public bool Occupied(GridPos cell)
            {
                foreach (var pair in _inventory.inventoryMatrix)
                {
                    if (pair.Key.x == cell.X && pair.Key.y == cell.Y) return pair.Value != null;
                }
                return false;
            }

            public string Swap(GridPos from, GridPos to)
            {
                try
                {
                    _inventory.Swap((sbyte)from.X, (sbyte)from.Y, (sbyte)to.X, (sbyte)to.Y);
                    return null;
                }
                catch (Exception ex)
                {
                    DiagnosticError ??= ex.ToString();
                    return ex.Message;
                }
            }

            /// <summary>자리는 사전의 열쇠에서 가져온다 - 석판의 좌표 필드가 아니다.</summary>
            public bool TryFindTablet(int instanceId, out GridPos cell, out int rotation, out bool rotatable)
            {
                foreach (var pair in _inventory.stoneTablets)
                {
                    var tablet = pair.Value;
                    if (tablet == null || tablet.instanceID != instanceId) continue;

                    cell = new GridPos(pair.Key.x, pair.Key.y);
                    rotation = tablet.rotation;
                    rotatable = DungeonManager.IsTabletRotatable(tablet.instanceID, tablet.isRotatable);
                    return true;
                }

                cell = default;
                rotation = 0;
                rotatable = false;
                return false;
            }

            public string Press(GridPos cell)
            {
                try
                {
                    _inventory.DoClickAction(new ItemPosition((sbyte)cell.X, (sbyte)cell.Y));
                    return null;
                }
                catch (Exception ex)
                {
                    DiagnosticError ??= ex.ToString();
                    return ex.Message;
                }
            }

            /// <summary>
            /// 호스트의 회전. <c>Permission</c> 이 닫힐 때 레벨 행렬이 다시 계산되므로 회전을 모아
            /// 한 번에 처리한다. 일부만 돌아간 채 남지 않게, 도중에 실패하면 이미 돌린 것을 원래
            /// 각도로 되돌린다.
            /// </summary>
            public string Rotate(IReadOnlyList<TabletTurn> turns)
            {
                var original = new List<KeyValuePair<StoneTablet, int>>();
                try
                {
                    using (new GridInventory.Permission(_inventory))
                    {
                        foreach (var turn in turns)
                        {
                            var tablet = TabletOf(turn.InstanceId);
                            if (tablet == null)
                                throw new InvalidOperationException($"석판(인스턴스 {turn.InstanceId})이 사라졌습니다");

                            original.Add(new KeyValuePair<StoneTablet, int>(tablet, tablet.rotation));
                            tablet.Networkrotation = turn.Rotation;
                        }
                    }
                    Announce(original);
                    return null;
                }
                catch (Exception ex)
                {
                    DiagnosticError ??= ex.ToString();
                    try
                    {
                        using (new GridInventory.Permission(_inventory))
                        {
                            foreach (var pair in original) pair.Key.Networkrotation = pair.Value;
                        }
                        Announce(original);
                        return $"회전 중 오류가 나 원래 각도로 되돌렸습니다({ex.Message}).";
                    }
                    catch (Exception restore)
                    {
                        DiagnosticError += "\n되돌리기 실패: " + restore;
                        return $"회전 중 오류가 났고 되돌리기도 실패했습니다({ex.Message} / {restore.Message}).";
                    }
                }
            }

            /// <summary>
            /// 게임이 <c>StoneTablet.Rotate</c> 끝에 보내는 알림을 우리도 보낸다. <b>커서가 올라가
            /// 있는 석판에만 보낸다.</b>
            ///
            /// 각도만 바꾸면 <c>Networkrotation</c> 이 SyncVar 라 값 자체는 퍼지지만, 화면의 석판
            /// 범위 테두리는 이 알림을 받아야 다시 그려진다
            /// (<c>OnTabletRotatedClientside</c> → <c>UI_CharacterStatusPanel.HandleTabletRotated</c> →
            /// <c>OnItemSelected</c>). 빠뜨리면 가방을 열어 둔 채 자동 배치했을 때 옛 각도의
            /// 테두리가 화면에 남고, 가방을 닫았다 열어야 사라진다.
            ///
            /// <b>그 <c>OnItemSelected</c> 는 테두리만 다시 그리지 않고 석판 툴팁까지 연다</b>
            /// (<c>UI_StoneTabletTooltip.Open</c>). 게임은 이 경로를 우클릭으로만, 즉 가방이 열려
            /// 있고 커서가 그 칸에 있을 때만 타므로 거기서는 맞는 동작이다. 커서와 무관하게 보내자
            /// 가방을 닫은 채 자동 배치했을 때 석판 설명이 툴팁 자리(화면 오른쪽)에 혼자 떠서
            /// 가방을 열었다 닫을 때까지 남았다 - 커서가 올라간 적이 없어 <c>Showing</c> 을 내려 줄
            /// 이벤트도 오지 않기 때문이다. 게임에서 <c>OnItemSelected</c> 를 부르는 다른 자리들은
            /// 모두 <c>currentSelectedGameObject</c> 를 먼저 보는데 <c>HandleTabletRotated</c> 에만
            /// 그 검사가 없다. 그래서 우리가 대신 한다.
            ///
            /// 테두리는 선택된 아이템의 범위 표시라 그 석판에 커서가 올라가 있을 때만 생기므로
            /// 게이트가 잃는 것은 없다. 반대로 다른 아이템을 보고 있을 때 보내면 그쪽 테두리와
            /// 툴팁을 빼앗는다.
            ///
            /// 참가자 경로는 <c>DoClickAction</c> 이 게임의 <c>Rotate</c> 를 그대로 타므로 해당
            /// 없다. 알림도 게임이 보내므로 그쪽 툴팁은 우리가 막을 수 없다. 되돌린 뒤에도 보낸다 -
            /// 그때는 되돌아간 각도가 화면에 맞아야 한다.
            /// </summary>
            private void Announce(IReadOnlyList<KeyValuePair<StoneTablet, int>> rotated)
            {
                var hovered = Hovered();
                if (hovered == null) return;

                foreach (var pair in rotated)
                {
                    if (pair.Key != hovered) continue;
                    try
                    {
                        _inventory.SendMessageTabletRotated(pair.Key, pair.Key.rotation);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticError ??= ex.ToString();
                        // 알림이 실패해도 각도는 이미 맞다. 화면만 낡은 채로 두고 넘어간다.
                        UnityEngine.Debug.LogWarning("석판 회전 알림 실패: " + ex.Message);
                    }
                }
            }

            /// <summary>
            /// 게임이 지금 선택으로 보는 칸의 석판. 가방이 닫혀 있거나 커서가 석판 위가 아니면
            /// <c>null</c> 이다.
            /// </summary>
            private static StoneTablet Hovered()
            {
                var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                if (selected == null) return null;

                var icon = selected.GetComponent<UI_NewInventoryIcon>();
                var item = icon == null ? null : icon.Item;
                return item?.StoneTablet;
            }

            public int LevelAt(GridPos cell)
            {
                _inventory.levelMatrix.TryGetValue(new ItemPosition((sbyte)cell.X, (sbyte)cell.Y), out var level);
                return level;
            }

            private StoneTablet TabletOf(int instanceId)
            {
                foreach (var pair in _inventory.stoneTablets)
                {
                    if (pair.Value != null && pair.Value.instanceID == instanceId) return pair.Value;
                }
                return null;
            }
        }
    }
}
