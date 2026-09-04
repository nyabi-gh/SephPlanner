using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;
using UnityEngine;

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
    /// 멀티 세션은 기본으로 잠그고 설정으로만 연다(실험). 동기화가 검증되지 않았고 개발사도
    /// 잠가 두는 편이 안전하다고 답했기 때문이다(docs/LEGAL.md).
    /// </summary>
    internal static class PlanApplier
    {
        /// <summary>클라이언트의 한 걸음이 서버를 돌아 반영될 때까지 기다리는 한계(초).</summary>
        private const float SyncTimeout = 3f;

        /// <summary>
        /// 한 국면(앞으로 가기, 되돌리기)이 통째로 쓸 수 있는 시간(초). 걸음마다의 한계만으로는
        /// 전체 상한이 없어, 서버가 조용하면 걸음 수 × 3초 동안 인벤토리가 저 혼자 움직인다.
        /// 되돌리기는 제 몫을 새로 받는다 - 앞이 시간을 다 썼다고 되돌리기를 굶기면 반쯤 적용된
        /// 배치가 남는다.
        /// </summary>
        private const float PhaseTimeout = 30f;

        private static bool _running;
        private static float _phaseUntil;
        private static float _abandonAt;

        /// <summary>
        /// 적용이 진행 중인가. 클라이언트에서는 여러 프레임에 걸치므로, 겹쳐 시작하면 두 적용기가
        /// 같은 격자를 서로 다른 예상 위에서 밀게 된다.
        ///
        /// 시한을 함께 보는 것은 유니티가 코루틴을 소유한 오브젝트가 비활성화될 때 반복자를
        /// <c>Dispose</c> 하지 않기 때문이다. 그러면 <c>finally</c> 가 돌지 않아 F8 이 세션 내내
        /// "진행 중"으로 잠긴다.
        /// </summary>
        public static bool InProgress => _running && Time.unscaledTime < _abandonAt;

        /// <summary>진행 상황 한 줄. 참가자 세션의 적용은 수 초가 걸리므로 화면에 이것이라도 있어야 한다.</summary>
        public static string Progress { get; private set; } = "";

        /// <summary>
        /// 배치를 적용하고 사람이 읽을 한 줄을 <paramref name="report"/> 로 돌려준다. 사전 검증에서
        /// 물러서면 아무것도 바꾸지 않은 상태고, 적용 도중 실패하면 이미 실행한 걸음을 역순으로
        /// 되돌린다. 되돌리기까지 실패한 경우에만 중간 상태가 남으며 그 사실이 결과에 그대로 적힌다.
        /// </summary>
        public static IEnumerator Apply(
            ApplyPlanCommand command, bool allowMultiplayer, Action<string> report)
        {
            if (InProgress)
            {
                report("이전 자동 배치가 아직 끝나지 않았습니다. 잠시 뒤 다시 누르세요.");
                yield break;
            }

            _running = true;
            NewPhase();
            try
            {
                var setup = Prepare(command, allowMultiplayer);
                if (setup.Failure != null)
                {
                    report(setup.Failure);
                    yield break;
                }

                var inventory = setup.Inventory;
                var journal = new List<Step>();

                // 안쪽 코루틴은 `yield return` 으로 넘기지 않고 직접 돌린다. 유니티에 넘기면
                // 기다릴 것이 없어도 단계마다 프레임을 쓰는데, 호스트의 적용은 예전처럼 키를
                // 누른 그 프레임에 통째로 끝나야 한다 - 중간에 프레임이 끼면 그 사이에 게임
                // 상태가 바뀔 수 있고, 그러면 원자적이라는 전제가 무너진다.
                //
                // 손으로 돌리는 덕에 예외도 여기서 잡힌다(`Advance`). 코루틴 본문에서는 잡을 수
                // 없고, 놓치면 이미 옮긴 걸음이 되돌려지지 않은 채 화면에 아무것도 뜨지 않는다.
                var moves = new Outcome();
                var swapping = ApplySwaps(
                    command, inventory, setup.Positions, setup.Occupants, journal, allowMultiplayer, moves);
                while (Advance(swapping, moves)) yield return swapping.Current;
                if (moves.Fault != null && moves.Failure == null)
                    moves.Failure = $"적용 도중 오류가 나 자동 배치를 중단했습니다({moves.Fault}).";

                if (moves.Failure != null)
                {
                    var undone = new Outcome();
                    var undo = Rollback(inventory, journal, undone);
                    while (Advance(undo, undone)) yield return undo.Current;
                    report(Join(moves.Failure, Undone(undone)));
                    yield break;
                }

                var rotations = new Outcome();
                if (NetworkServer.active)
                {
                    RotateHere(command, inventory, rotations);
                }
                else
                {
                    var turning = RotateOverNetwork(command, inventory, rotations);
                    while (Advance(turning, rotations)) yield return turning.Current;
                    if (rotations.Fault != null && rotations.Failure == null)
                        rotations.Failure = $"회전 중 오류가 나 자동 배치를 중단했습니다({rotations.Fault}).";
                }

                if (rotations.Failure != null)
                {
                    // 옮겨졌지만 안 돌아간 배치는 솔버가 평가한 적 없는 상태다. 이동도 함께 되돌린다.
                    var undone = new Outcome();
                    var undo = Rollback(inventory, journal, undone);
                    while (Advance(undo, undone)) yield return undo.Current;
                    report(Join(rotations.Failure, rotations.Note, Undone(undone)));
                    yield break;
                }

                // 레벨은 서버가 다시 계산해 따로 동기화한다. 참가자 세션에서는 마지막 걸음이 반영된
                // 뒤에도 잠시 옛 값이 남아 있으므로, 어긋났다고 알리기 전에 그만큼 기다린다.
                Progress = "자동 배치 - 레벨 확인 중";
                var settle = Time.unscaledTime + (NetworkServer.active ? 0f : SyncTimeout);
                while (LevelDrift(command, inventory).Length > 0 && Time.unscaledTime < settle)
                    yield return null;

                report($"자동 배치 완료 - 이동 {moves.Count}건, 회전 {rotations.Count}건" +
                       LevelDrift(command, inventory));
            }
            finally
            {
                _running = false;
                Progress = "";
            }
        }

        /// <summary>
        /// 안쪽 코루틴을 한 걸음 돌린다. 예외를 여기서 잡는 것이 이 함수가 있는 이유다 - 반복자
        /// 본문에는 <c>try/catch</c> 를 둘 수 없고, 참가자 세션에서는 걸음 사이가 수 초라 그동안
        /// 인벤토리가 파괴될 수 있다(죽음, 층 이동, 접속 끊김).
        /// </summary>
        private static bool Advance(IEnumerator routine, Outcome outcome)
        {
            try
            {
                return routine.MoveNext();
            }
            catch (Exception ex)
            {
                outcome.Fault = ex.Message;
                return false;
            }
        }

        /// <summary>한 국면에 시간 예산을 새로 준다.</summary>
        private static void NewPhase()
        {
            _phaseUntil = Time.unscaledTime + PhaseTimeout;
            _abandonAt = _phaseUntil + SyncTimeout;
        }

        /// <summary>
        /// 이 걸음의 마감. 호스트는 쓰기가 그 자리에서 끝나므로 기다릴 것이 없어 지금이 곧 마감이다.
        /// </summary>
        private static float Deadline() =>
            NetworkServer.active
                ? Time.unscaledTime
                : Mathf.Min(Time.unscaledTime + SyncTimeout, _phaseUntil);

        private static string Join(params string[] parts)
        {
            var text = "";
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                text = text.Length == 0 ? part : text + " " + part;
            }
            return text;
        }

        private static string Undone(Outcome outcome) =>
            outcome.Fault != null
                ? $"되돌리는 중 오류가 나 인벤토리가 중간 상태로 남았습니다({outcome.Fault}). " +
                  "손으로 정리한 뒤 다시 시도하세요."
                : outcome.Note;

        /// <summary>코루틴이 값을 돌려줄 자리.</summary>
        private sealed class Outcome
        {
            /// <summary>실패했으면 사람이 읽을 이유. 성공이면 null.</summary>
            public string Failure;

            /// <summary>해낸 걸음 수.</summary>
            public int Count;

            /// <summary>되돌리기 결과처럼 결과에 덧붙일 한 마디.</summary>
            public string Note;

            /// <summary><see cref="Advance"/>가 잡은 예외 메시지.</summary>
            public string Fault;
        }

        private sealed class Preparation
        {
            public string Failure;
            public GridInventory Inventory;
            public Dictionary<int, GridPos> Positions;
            public Dictionary<GridPos, int> Occupants;
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
                    return Denied("석판 질의 검증이 완료되지 않았거나 실패해 자동 배치를 실행하지 않습니다. F9로 데이터를 다시 만드세요.");
                if (command.ExpectedCatalogGeneration.Length == 0 ||
                    command.ExpectedCatalogGeneration != CatalogDump.ActiveGeneration)
                    return Denied("계획을 만든 카탈로그가 더 이상 최신이 아니어서 자동 배치를 실행하지 않습니다.");
                if (command.ExpectedPlacementFingerprint.Length == 0 ||
                    command.ExpectedPlanningContextFingerprint.Length == 0)
                    return Denied("계획의 상태 지문이 없어 자동 배치를 실행하지 않습니다.");

                if (GameReader.IsMultiplayerSession() && !allowMultiplayer)
                    return Denied("멀티플레이 세션에서는 자동 배치를 실행하지 않습니다. 설정에서 열 수 있습니다(실험).");

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
                var error = Validate(
                    command, inventory, livePlacementFingerprint, liveWeaponId,
                    out var positions, out var occupants);
                if (error != null) return Denied(error);

                return new Preparation
                {
                    Inventory = inventory,
                    Positions = positions,
                    Occupants = occupants,
                };
            }
            catch (Exception ex)
            {
                return Denied($"자동 배치를 준비하다 오류가 나 아무것도 바꾸지 않았습니다({ex.Message}).");
            }
        }

        private static Preparation Denied(string reason) => new Preparation { Failure = reason };

        /// <summary>
        /// 적용이 끝난 상태의 레벨이 계산과 같은지. 어긋나면 우리가 읽지 않는 효과가 걸려 있다는
        /// 뜻이다. 배치 자체는 합법이라 되돌리지 않고 알리기만 한다.
        /// </summary>
        private static string LevelDrift(ApplyPlanCommand command, GridInventory inventory)
        {
            // 인벤토리가 사라졌으면 견줄 것이 없다. 걸음은 이미 반영된 뒤이므로 실패가 아니다.
            if (inventory == null) return "";

            var mismatches = 0;
            var first = "";
            foreach (var pair in command.ExpectedCellLevels)
            {
                inventory.levelMatrix.TryGetValue(
                    new ItemPosition((sbyte)pair.Key.X, (sbyte)pair.Key.Y), out var reported);
                if (reported == pair.Value) continue;

                mismatches++;
                if (first.Length == 0) first = $"{pair.Key} 계산 {pair.Value} != 게임 {reported}";
            }

            return mismatches == 0
                ? ""
                : $". 적용 뒤 레벨이 예상과 다른 칸 {mismatches}개({first}) - 계산에 없는 효과가 걸려 있습니다";
        }

        /// <summary>
        /// 실행한 맞바꿈 하나. 되돌린 뒤 두 칸에 무엇이 있어야 하는지까지 들고 있어야 되돌리기가
        /// 실제로 됐는지 볼 수 있다.
        /// </summary>
        private readonly struct Step
        {
            public Step(GridPos from, GridPos to, int instanceId, int displaced)
            {
                From = from;
                To = to;
                InstanceId = instanceId;
                Displaced = displaced;
            }

            public GridPos From { get; }
            public GridPos To { get; }
            public int InstanceId { get; }

            /// <summary>이 걸음이 밀어낸 인스턴스. 목표 칸이 비어 있었으면 0.</summary>
            public int Displaced { get; }
        }

        /// <summary>
        /// 돌려야 할 석판 하나. 사라진 뒤에도 무엇이었는지 댈 수 있게 인스턴스를, 우클릭을 어느
        /// 칸에 보낼지 정하려고 자리를 함께 든다.
        /// </summary>
        private readonly struct Turn
        {
            public Turn(StoneTablet tablet, int instanceId, GridPos cell, int presses)
            {
                Tablet = tablet;
                InstanceId = instanceId;
                Cell = cell;
                Presses = presses;
            }

            public StoneTablet Tablet { get; }
            public int InstanceId { get; }
            public GridPos Cell { get; }
            public int Presses { get; }
        }

        private static string Validate(
            ApplyPlanCommand command, GridInventory inventory,
            string livePlacementFingerprint, string liveWeaponId,
            out Dictionary<int, GridPos> positions, out Dictionary<GridPos, int> occupants)
        {
            positions = new Dictionary<int, GridPos>();
            occupants = new Dictionary<GridPos, int>();

            // 칸 수는 격자 안팎을 가리지 않고 센다. 열린 격자 경계에 걸친 아이템을 격자 안 칸만으로
            // 세면 한 칸짜리로 보여 가드를 빠져나가고, 한 칸짜리 맞바꿈이 그 아이템을 찢는다.
            var grid = GameReader.GridOf(inventory);
            var cellCounts = new Dictionary<int, int>();
            var touchesGrid = new HashSet<int>();
            foreach (var pair in inventory.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null) continue;

                cellCounts[instance.InstanceID] =
                    cellCounts.TryGetValue(instance.InstanceID, out var count) ? count + 1 : 1;
                if (!grid.Contains(pair.Key.x, pair.Key.y)) continue;

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

        private static IEnumerator ApplySwaps(
            ApplyPlanCommand command, GridInventory inventory,
            Dictionary<int, GridPos> positions, Dictionary<GridPos, int> occupants,
            List<Step> journal, bool allowMultiplayer, Outcome outcome)
        {
            var total = command.Targets.Count;
            foreach (var target in command.Targets)
            {
                var from = positions[target.InstanceId];
                var to = target.To;
                if (from == to) continue;

                if (inventory == null)
                {
                    outcome.Failure = "적용 도중 인벤토리가 사라져 자동 배치를 중단했습니다.";
                    yield break;
                }

                // 적용을 시작한 뒤에 동료가 접속했을 수 있다. 멀티가 된 순간 더 진행하지 않는다.
                if (!allowMultiplayer && GameReader.IsMultiplayerSession())
                {
                    outcome.Failure = "적용 도중 멀티플레이 세션이 되어 자동 배치를 중단했습니다.";
                    yield break;
                }

                var displaced = occupants.TryGetValue(to, out var occupant);
                var expected = displaced ? occupant : 0;

                var error = Swap(inventory, from, to);
                if (error != null)
                {
                    outcome.Failure = $"이동 중 오류({error})가 나 자동 배치를 중단했습니다.";
                    yield break;
                }

                // 호스트의 쓰기는 그 자리에서 끝나므로 기다릴 것이 없다. 참가자 세션에서는 CmdSwap
                // 이 서버를 돌아 SyncDictionary 로 돌아올 때까지 아직 아무것도 바뀌지 않았다.
                var deadline = Deadline();
                while (inventory != null && !Swapped(inventory, from, to, target.InstanceId, expected) &&
                       Time.unscaledTime < deadline)
                {
                    yield return null;
                }

                // 게임의 LocalSwap 은 쓰기 권한이 없거나 포션 줄이면 예외 없이 로그만 남기고
                // 돌아온다. 그런 걸음을 저널에 올리면 나중 되돌리기가 "되돌리기"가 아니라
                // "처음 적용"이 되어 원래 배치와 다른 순열을 남긴다. 실제로 옮겨졌는지 본다.
                if (inventory == null || !Swapped(inventory, from, to, target.InstanceId, expected))
                {
                    outcome.Failure = $"{from} → {to} 이동이 게임에 반영되지 않아 자동 배치를 중단했습니다.";
                    yield break;
                }

                journal.Add(new Step(from, to, target.InstanceId, expected));
                outcome.Count++;
                Progress = $"자동 배치 중 - 이동 {outcome.Count}/{total}";

                if (displaced)
                {
                    occupants[from] = occupant;
                    positions[occupant] = from;
                }
                else
                {
                    occupants.Remove(from);
                }
                occupants[to] = target.InstanceId;
                positions[target.InstanceId] = to;
            }
        }

        /// <summary>맞바꿈 한 번. 예외를 메시지로 바꿔 돌려준다 - 코루틴 안에서는 잡을 수 없다.</summary>
        private static string Swap(GridInventory inventory, GridPos from, GridPos to)
        {
            try
            {
                inventory.Swap((sbyte)from.X, (sbyte)from.Y, (sbyte)to.X, (sbyte)to.Y);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// 그 걸음이 실제로 반영됐는가. 맞바꿈이라 밀려난 쪽까지 제자리에 와야 끝난 것이다 -
        /// 옮긴 쪽만 보면 참가자 세션에서 절반만 도착한 상태를 완료로 읽을 수 있다.
        /// </summary>
        private static bool Swapped(
            GridInventory inventory, GridPos from, GridPos to, int moved, int displaced) =>
            InstanceAt(inventory, to) == moved && InstanceAt(inventory, from) == displaced;

        /// <summary>그 칸에 지금 있는 인스턴스. 비어 있으면 0.</summary>
        private static int InstanceAt(GridInventory inventory, GridPos cell)
        {
            foreach (var pair in inventory.inventoryMatrix)
            {
                if (pair.Key.x == cell.X && pair.Key.y == cell.Y)
                    return pair.Value != null ? pair.Value.InstanceID : 0;
            }
            return 0;
        }

        /// <summary>
        /// 실행한 맞바꿈을 역순으로 재생해 원래 배치로 되돌린다. Swap 은 자기 자신이 역연산이라
        /// 이전 상태를 따로 저장할 필요가 없다.
        /// </summary>
        private static IEnumerator Rollback(
            GridInventory inventory, List<Step> journal, Outcome outcome)
        {
            if (journal.Count == 0) yield break;

            NewPhase();
            Progress = "자동 배치 - 되돌리는 중";
            for (var i = journal.Count - 1; i >= 0; i--)
            {
                if (inventory == null)
                {
                    outcome.Note = "되돌리는 중 인벤토리가 사라져 중간 상태로 남았습니다.";
                    yield break;
                }

                var step = journal[i];
                var error = Swap(inventory, step.To, step.From);
                if (error != null)
                {
                    outcome.Note = $"되돌리기도 실패해 인벤토리가 중간 상태로 남았습니다({error}). " +
                                   "손으로 정리한 뒤 다시 시도하세요.";
                    yield break;
                }

                var deadline = Deadline();
                while (inventory != null &&
                       !Swapped(inventory, step.To, step.From, step.InstanceId, step.Displaced) &&
                       Time.unscaledTime < deadline)
                {
                    yield return null;
                }

                // 앞으로 가는 길과 같은 이유다. LocalSwap 은 거부해도 예외 없이 돌아오므로,
                // 확인하지 않으면 거부된 되돌리기를 "원래 배치로 되돌렸습니다"로 보고하게 된다.
                if (inventory == null ||
                    !Swapped(inventory, step.To, step.From, step.InstanceId, step.Displaced))
                {
                    outcome.Note = "되돌리기가 게임에 반영되지 않아 인벤토리가 중간 상태로 남았습니다. " +
                                   "손으로 정리한 뒤 다시 시도하세요.";
                    yield break;
                }
            }
            outcome.Note = "원래 배치로 되돌렸습니다.";
        }

        /// <summary>
        /// 호스트의 회전. 각도를 그대로 쓴다. <c>Permission</c> 이 닫힐 때 레벨 행렬이 다시
        /// 계산되므로 회전을 모아 한 번에 처리한다.
        /// </summary>
        private static void RotateHere(
            ApplyPlanCommand command, GridInventory inventory, Outcome outcome)
        {
            var pending = new List<Turn>();
            var error = Collect(command, inventory, pending);
            if (error != null)
            {
                outcome.Failure = error;
                return;
            }
            if (pending.Count == 0) return;

            var original = new List<KeyValuePair<StoneTablet, int>>();
            try
            {
                using (new GridInventory.Permission(inventory))
                {
                    foreach (var turn in pending)
                    {
                        original.Add(new KeyValuePair<StoneTablet, int>(turn.Tablet, turn.Tablet.rotation));

                        // 여기서는 각도를 그대로 줄 수 있다. 걸음 수로 셈하는 것은 참가자 쪽과
                        // 같은 값을 쓰기 위해서고, 결과는 목표 각도를 0~3 으로 접은 것과 같다.
                        turn.Tablet.Networkrotation =
                            (turn.Tablet.rotation + turn.Presses) % TabletRotation.Steps;
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
                    outcome.Failure = $"회전 중 오류가 나 원래 각도로 되돌렸습니다({ex.Message}).";
                }
                catch (Exception restore)
                {
                    outcome.Failure = $"회전 중 오류가 났고 되돌리기도 실패했습니다({ex.Message} / {restore.Message}).";
                }
                return;
            }
            outcome.Count = pending.Count;
        }

        /// <summary>
        /// 참가자 세션의 회전. 각도를 직접 쓰는 길이 없다 - <c>Networkrotation</c> 은 SyncVar 라
        /// 클라이언트에서 써 봐야 서버 값이 덮는다. 대신 우클릭이 타는 <c>DoClickAction</c> 을
        /// 필요한 횟수만큼 누른다. 서버가 한 번에 90도씩 돌리므로 누를 때마다 반영을 기다리고,
        /// 도중에 막히면 눌러 둔 만큼 마저 눌러 원래 각도로 되돌린다.
        /// </summary>
        private static IEnumerator RotateOverNetwork(
            ApplyPlanCommand command, GridInventory inventory, Outcome outcome)
        {
            var pending = new List<Turn>();
            var error = Collect(command, inventory, pending);
            if (error != null)
            {
                outcome.Failure = error;
                yield break;
            }
            if (pending.Count == 0) yield break;

            var pressed = new List<Turn>();
            foreach (var turn in pending)
            {
                var count = 0;
                string failure = null;
                while (count < turn.Presses)
                {
                    var step = new Outcome();
                    var pressing = PressOnce(inventory, turn, step);
                    while (Advance(pressing, step)) yield return pressing.Current;

                    var trouble = step.Fault != null ? $"회전 중 오류({step.Fault})" : step.Failure;
                    if (trouble != null)
                    {
                        failure = trouble + " - 자동 배치를 중단합니다.";
                        break;
                    }
                    count++;
                    Progress = $"자동 배치 중 - 회전 {outcome.Count + 1}/{pending.Count}";
                }

                pressed.Add(new Turn(turn.Tablet, turn.InstanceId, turn.Cell, count));
                if (failure == null)
                {
                    outcome.Count++;
                    continue;
                }

                outcome.Failure = failure;
                var undone = new Outcome();
                var restoring = RestoreRotations(inventory, pressed, undone);
                while (Advance(restoring, undone)) yield return restoring.Current;
                outcome.Note = undone.Fault != null
                    ? $"각도를 되돌리는 중 오류가 났습니다({undone.Fault})."
                    : undone.Note;
                yield break;
            }
        }

        /// <summary>눌러 둔 만큼 마저 눌러 한 바퀴를 채운다. 회전이 4 걸음이라 그것이 역연산이다.</summary>
        private static IEnumerator RestoreRotations(
            GridInventory inventory, List<Turn> pressed, Outcome outcome)
        {
            NewPhase();
            Progress = "자동 배치 - 각도를 되돌리는 중";
            foreach (var turn in pressed)
            {
                var back = TabletRotation.PressesBack(turn.Presses);
                for (var i = 0; i < back; i++)
                {
                    var step = new Outcome();
                    var pressing = PressOnce(inventory, turn, step);
                    while (Advance(pressing, step)) yield return pressing.Current;

                    var trouble = step.Fault != null ? $"오류({step.Fault})" : step.Failure;
                    if (trouble == null) continue;

                    // 조용히 물러서면 "원래 배치로 되돌렸습니다"라고 보고하면서 석판 하나를
                    // 엉뚱한 각도로 남기게 된다.
                    outcome.Note = $"석판(인스턴스 {turn.InstanceId})의 각도를 되돌리지 못했습니다({trouble}).";
                    yield break;
                }
            }
        }

        /// <summary>
        /// 회전 한 걸음. 게임이 우클릭에 쓰는 그 경로이므로 <b>칸</b>을 누른다 - 그래서 누르기
        /// 직전에 그 칸이 아직 이 석판의 것인지 본다. <c>xIdx</c>/<c>yIdx</c> 는 우리가 걸음마다
        /// 기다리는 <c>inventoryMatrix</c> 와 별개로 동기화되므로, 믿고 누르면 엉뚱한 칸을
        /// 우클릭할 수 있고 그것은 되돌릴 방법도 알 방법도 없다.
        /// </summary>
        private static IEnumerator PressOnce(GridInventory inventory, Turn turn, Outcome outcome)
        {
            if (inventory == null || turn.Tablet == null)
            {
                outcome.Failure = $"석판(인스턴스 {turn.InstanceId})이 사라졌습니다";
                yield break;
            }
            if (InstanceAt(inventory, turn.Cell) != turn.InstanceId)
            {
                outcome.Failure = $"석판(인스턴스 {turn.InstanceId})이 {turn.Cell} 에 없습니다";
                yield break;
            }

            var before = turn.Tablet.rotation;
            var error = Click(inventory, turn.Cell);
            if (error != null)
            {
                outcome.Failure = $"회전 중 오류({error})";
                yield break;
            }

            var deadline = Deadline();
            while (turn.Tablet != null && turn.Tablet.rotation == before &&
                   Time.unscaledTime < deadline)
            {
                yield return null;
            }

            if (turn.Tablet == null || turn.Tablet.rotation == before)
                outcome.Failure = $"석판(인스턴스 {turn.InstanceId}) 회전이 시간 안에 반영되지 않았습니다";
        }

        private static string Click(GridInventory inventory, GridPos cell)
        {
            try
            {
                inventory.DoClickAction(new ItemPosition((sbyte)cell.X, (sbyte)cell.Y));
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// 돌려야 할 석판과 횟수를 모은다. 솔버도 스냅샷의 인스턴스별 회전 가능 여부를 보지만,
        /// 계산 이후 저주 등으로 잠겼을 수 있어 쓰기 직전에 한 번 더 확인한다.
        /// </summary>
        private static string Collect(
            ApplyPlanCommand command, GridInventory inventory, List<Turn> pending)
        {
            try
            {
                foreach (var target in command.Targets)
                {
                    if (!target.IsTablet) continue;

                    if (!TryFindTablet(inventory, target.InstanceId, out var tablet, out var cell))
                        return "적용 도중 석판이 사라져 자동 배치를 중단합니다.";

                    var presses = TabletRotation.PressesFrom(tablet.rotation, target.Rotation);
                    if (presses == 0) continue;

                    if (!DungeonManager.IsTabletRotatable(tablet.instanceID, tablet.isRotatable))
                        return $"적용 도중 석판(인스턴스 {target.InstanceId})의 회전이 잠겨 중단합니다.";

                    pending.Add(new Turn(tablet, target.InstanceId, cell, presses));
                }
                return null;
            }
            catch (Exception ex)
            {
                // 아직 아무것도 쓰지 않았다. 회전만 포기하면 이동을 되돌릴지 호출자가 정한다.
                return $"회전 준비 중 오류가 났습니다({ex.Message}).";
            }
        }

        /// <summary>석판과 그 자리. 자리는 사전의 열쇠에서 가져온다 - 석판의 좌표 필드가 아니다.</summary>
        private static bool TryFindTablet(
            GridInventory inventory, int instanceId, out StoneTablet tablet, out GridPos cell)
        {
            foreach (var pair in inventory.stoneTablets)
            {
                if (pair.Value == null || pair.Value.instanceID != instanceId) continue;

                tablet = pair.Value;
                cell = new GridPos(pair.Key.x, pair.Key.y);
                return true;
            }

            tablet = null;
            cell = default;
            return false;
        }
    }
}
