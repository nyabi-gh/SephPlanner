using System;
using System.Collections;
using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 검증이 끝난 계획을 인벤토리에 적용하는 순서. 이동을 저널에 남기며 하나씩 하고, 그다음
    /// 회전을 한다. 확정된 실패는 저널을 역순으로 되돌리되, 미확정 쓰기나 외부 변경에는 추가 쓰기를
    /// 멈춘다. 걸음마다 반영을 확인하므로 게임이 조용히 거부한 걸음을 성공으로 보고하지 않는다.
    ///
    /// 반복자다. 기다릴 것이 있으면 null 을 내놓고 다시 불리면 이어서 간다. 호스트에서는 쓰기가
    /// 그 자리에서 끝나 한 번도 멈추지 않는다 - 키를 누른 그 프레임에 통째로 끝나야 그 사이에
    /// 게임 상태가 바뀔 틈이 없다. 게임 타입은 <see cref="IInventoryPort"/> 뒤에 있다.
    /// </summary>
    public sealed class ApplyPlanRoutine
    {
        /// <summary>참가자의 한 걸음이 서버를 돌아 반영될 때까지 기다리는 한계(초).</summary>
        public const double SyncTimeout = 3;

        /// <summary>
        /// 한 국면(앞으로 가기, 되돌리기)이 통째로 쓸 수 있는 시간(초). 걸음마다의 한계만으로는
        /// 전체 상한이 없어, 서버가 조용하면 걸음 수 × 3초 동안 인벤토리가 저 혼자 움직인다.
        /// 되돌리기는 제 몫을 새로 받는다 - 앞이 시간을 다 썼다고 되돌리기를 굶기면 반쯤 적용된
        /// 배치가 남는다.
        ///
        /// 예산은 걸음을 <b>보내기 전에</b> 본다. 이미 보낸 걸음의 3초 창을 중간에 자르면, 확인이
        /// 안 된 채로 저널에 못 올라간 걸음이 그 뒤에 반영되어 되돌리기가 그것을 놓친다. 그래서
        /// 한 국면은 길어야 이 값에 걸음 하나의 창을 더한 만큼이다.
        /// </summary>
        public const double PhaseTimeout = 30;

        private readonly ApplyPlanCommand _command;
        private readonly IInventoryPort _port;
        private readonly Func<double> _now;
        private readonly bool _allowMultiplayer;
        private double _phaseUntil;
        private int _levelsBefore;
        private bool? _collapsed;
        private string? _stateFailure;

        public ApplyPlanRoutine(
            ApplyPlanCommand command, IInventoryPort port, Func<double> now, bool allowMultiplayer)
        {
            _command = command;
            _port = port;
            _now = now;
            _allowMultiplayer = allowMultiplayer;
        }

        /// <summary>진행 상황 한 줄. 참가자 세션의 적용은 수 초가 걸리므로 화면에 이것이라도 있어야 한다.</summary>
        public string Progress { get; private set; } = "";

        /// <summary>
        /// 이 시각을 넘기면 반복자가 죽은 것으로 보아도 된다. 유니티는 코루틴을 소유한 오브젝트가
        /// 비활성화될 때 반복자를 Dispose 하지 않아, 부르는 쪽이 "진행 중" 을 영영 풀지 못할 수 있다.
        /// </summary>
        public double AbandonAt { get; private set; }

        /// <summary>끝났을 때 사람이 읽을 한 줄. 반복자가 끝나기 전에는 비어 있다.</summary>
        public string Result { get; private set; } = "";

        /// <summary>쓰기 반영이 불명확하거나 효과 연결이 깨졌다. 같은 인벤토리에 추가 쓰기를 보내면 안 된다.</summary>
        public bool RequiresResync { get; private set; }
        public string? DiagnosticError { get; private set; }

        /// <summary>
        /// 계획대로 다 놓였고 레벨까지 예상과 같다. <b>이때만</b> 부르는 쪽이 "지금 놓인 것이 곧
        /// 그 계획" 이라고 믿어도 된다 - 실행기가 그것으로 다음 재계산을 건너뛴다. 중간에 멈췄거나
        /// 서버 반영이 불확실하면 무엇이 놓였는지 우리가 모르므로 거짓이다.
        /// </summary>
        public bool Settled { get; private set; }

        private const string TidyMessage = "손으로 정리한 뒤 다시 시도하세요.";

        private const string CollapsedMessage =
            "게임이 계산한 칸 레벨이 전부 0 이 됐습니다 - 가방의 효과가 꺼진 상태입니다. " +
            "동기화 중이면 곧 돌아오고, 그대로 남으면 손으로 옮겨도 같은 오류가 되풀이되니 " +
            "방을 나갔다 들어오거나 게임을 다시 시작하세요. F10 으로 진단을 남겨 주시면 원인을 찾는 데 도움이 됩니다.";

        private const string UncertainMessage =
            "서버 반영 여부를 확인하지 못했습니다. 늦게 적용될 수 있어 추가 이동과 되돌리기를 멈췄습니다. " +
            "자동 배치를 다시 쓰려면 방에 재접속하세요.";

        /// <summary>
        /// 사전 검증을 통과한 뒤 부른다. 실패하면 안전하게 확인된 걸음만 되돌리고,
        /// 미확정 쓰기나 외부 변경 때문에 중간 상태가 남으면 <see cref="Result"/>에 알린다.
        /// </summary>
        public IEnumerator Run()
        {
            if (InvalidState() != null)
            {
                Result = _stateFailure!;
                yield break;
            }
            NewPhase();
            _levelsBefore = CountLevels();
            var journal = new List<Step>();

            // 안쪽 반복자는 직접 돌린다. 예외를 여기서 잡기 위해서다(Advance) - 반복자 본문에는
            // try/catch 를 둘 수 없고, 놓치면 이미 옮긴 걸음이 되돌려지지 않은 채 아무 말도 없다.
            var moves = new Outcome();
            var swapping = ApplySwaps(journal, moves);
            while (Advance(swapping, moves)) yield return null;
            if (moves.Fault != null && moves.Failure == null)
                moves.Failure = $"적용 도중 오류가 나 자동 배치를 중단했습니다({moves.Fault}).";

            if (moves.Failure != null)
            {
                if (RequiresResync)
                {
                    Result = Join(moves.Failure, _stateFailure == null ? UncertainMessage : null);
                    yield break;
                }
                var undone = new Outcome();
                var undo = Rollback(journal, undone);
                while (Advance(undo, undone)) yield return null;
                // 무너짐은 여기서 한 번만 붙인다. 되돌리기가 어느 가지로 빠져나가든 - 되돌릴
                // 걸음이 없었든, 칸이 바뀌어 멈췄든 - 안내가 빠지지 않게 하려는 것이다.
                Result = Join(moves.Failure, Undone(undone), Collapsed(),
                    RequiresResync && _stateFailure == null ? UncertainMessage : null);
                yield break;
            }

            var rotations = new Outcome();
            var turning = Rotate(rotations);
            while (Advance(turning, rotations)) yield return null;
            if (rotations.Fault != null && rotations.Failure == null)
                rotations.Failure = $"회전 중 오류가 나 자동 배치를 중단했습니다({rotations.Fault}).";

            if (rotations.Failure != null)
            {
                if (RequiresResync)
                {
                    Result = Join(rotations.Failure, UncertainMessage);
                    yield break;
                }
                // 옮겨졌지만 안 돌아간 배치는 솔버가 평가한 적 없는 상태다. 이동도 함께 되돌린다.
                var undone = new Outcome();
                var undo = Rollback(journal, undone);
                while (Advance(undo, undone)) yield return null;
                Result = Join(
                    rotations.Failure, rotations.Note, Undone(undone), Collapsed(),
                    RequiresResync && _stateFailure == null ? UncertainMessage : null);
                yield break;
            }

            var targetDrift = TargetDrift();
            if (targetDrift != null)
            {
                Result = targetDrift;
                yield break;
            }

            // 레벨은 서버가 다시 계산해 따로 동기화한다. 참가자 세션에서는 마지막 걸음이 반영된
            // 뒤에도 잠시 옛 값이 남아 있으므로, 어긋났다고 알리기 전에 그만큼 기다린다.
            Progress = "자동 배치 - 레벨 확인 중";
            var settle = _now() + (_port.WritesLandImmediately ? 0 : SyncTimeout);
            while (LevelDrift().Length > 0 && _now() < settle) yield return null;

            var drift = TargetDrift();

            var levels = drift is null ? LevelDrift() : "";
            // 예상도 전부 0이면 정상 배치다. 예상과 다른 소실만 별도 복구 안내를 붙인다.
            var collapsed = drift is null && levels.Length == 0 ? null : Collapsed();
            if (collapsed != null) levels = "";
            if (levels.Length > 0) DiagnosticError ??= levels;
            Settled = drift is null && collapsed is null && levels.Length == 0 && !RequiresResync;
            Result = Join(
                drift ?? $"자동 배치 완료 - 이동 {moves.Count}건, 회전 {rotations.Count}건" + levels, collapsed);
        }

        private string? TargetDrift()
        {
            try
            {
                if (!_port.Alive) return "인벤토리가 사라져 자동 배치 결과를 확인하지 못했습니다.";
                foreach (var target in _command.Targets)
                {
                    // 번호 없는 아이템은 번호로 확인할 수 없다. 그 칸이 차 있는지만 본다 -
                    // 애초에 못 옮기는 것이라 비었다면 우리가 아니라 게임이 치운 것이다.
                    if (target.Immovable)
                    {
                        if (!_port.Occupied(target.To))
                            return "적용 중 인벤토리가 바뀌어 목표 배치와 다릅니다. 현재 배치를 확인하세요.";
                        continue;
                    }

                    if (_port.InstanceAt(target.To) != target.InstanceId)
                        return "적용 중 인벤토리가 바뀌어 목표 배치와 다릅니다. 현재 배치를 확인하세요.";
                    if (target.IsTablet && RotationOf(target.InstanceId) != target.Rotation)
                        return "석판의 최종 각도가 목표와 다릅니다. 현재 배치를 확인하세요.";
                }
                return null;
            }
            catch (Exception ex)
            {
                DiagnosticError ??= ex.ToString();
                return $"자동 배치 결과를 확인하지 못했습니다({ex.Message}). 현재 배치를 확인하세요.";
            }
        }

        /// <summary>
        /// 안쪽 반복자를 한 걸음 돌린다. 참가자 세션에서는 걸음 사이가 수 초라 그동안 인벤토리가
        /// 파괴될 수 있고(죽음, 층 이동, 접속 끊김), 그때 읽기가 던지는 예외가 여기서 잡힌다.
        /// </summary>
        private bool Advance(IEnumerator routine, Outcome outcome)
        {
            try
            {
                return routine.MoveNext();
            }
            catch (Exception ex)
            {
                DiagnosticError ??= ex.ToString();
                outcome.Fault = ex.Message;
                return false;
            }
        }

        /// <summary>한 국면에 시간 예산을 새로 준다.</summary>
        private void NewPhase()
        {
            _phaseUntil = _now() + PhaseTimeout;
            AbandonAt = _phaseUntil + SyncTimeout;
        }

        /// <summary>이 걸음의 마감. 호스트는 쓰기가 그 자리에서 끝나므로 기다릴 것이 없어 지금이 곧 마감이다.</summary>
        private double Deadline() => _port.WritesLandImmediately ? _now() : _now() + SyncTimeout;

        /// <summary>이 국면에 걸음을 하나 더 보낼 예산이 남았는가. 호스트는 기다리지 않으므로 늘 참이다.</summary>
        private bool BudgetLeft() => _port.WritesLandImmediately || _now() < _phaseUntil;

        private static string Join(params string?[] parts)
        {
            var text = "";
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                text = text.Length == 0 ? part! : text + " " + part;
            }
            return text;
        }

        private string? Undone(Outcome outcome) =>
            outcome.Fault != null
                ? Join($"되돌리는 중 오류가 나 인벤토리가 중간 상태로 남았습니다({outcome.Fault}).", Recovery())
                : outcome.Note;

        /// <summary>
        /// 게임이 계산해 둔 칸 레벨 중 0 이 아닌 것의 수. 읽지 못하면 -1 이다 - 모르는 것과
        /// 비어 있는 것을 같이 두면 아래 판정이 없는 일을 있다고 하게 된다.
        /// </summary>
        private int CountLevels()
        {
            if (!_port.Alive) return -1;
            try
            {
                var grid = new GridSpec(_command.ExpectedWidth, _command.ExpectedHeight, _command.ExpectedStorage);
                var levels = 0;
                for (var index = 0; index < grid.Storage; index++)
                {
                    if (_port.LevelAt(grid.ToPosition(index)) != 0) levels++;
                }
                return levels;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// 있던 레벨이 통째로 사라졌는가. 게임은 맞바꿈 끝에 레벨 행렬을 다시 만드는데, 그것이
        /// 도중에 예외로 멈추면 석판이 전부 꺼지고 칸 레벨이 0 으로 남는다(제보 5915982c,
        /// 2026-09-15 - 맞바꿈도 되돌리기도 같은 예외로 끝났다). 그 상태를 알아야 하는 이유는
        /// 안내가 달라지기 때문이다.
        /// </summary>
        private string? Collapsed()
        {
            // 한 번의 실패 안에서 답이 바뀌지 않는다. 가지마다 다시 재면 격자를 몇 번씩 훑는다.
            _collapsed ??= _levelsBefore > 0 && CountLevels() == 0;
            if (_collapsed.Value) DiagnosticError ??= CollapsedMessage;
            return _collapsed.Value ? CollapsedMessage : null;
        }

        /// <summary>
        /// 중간 상태가 남았을 때 무엇을 하라고 할 것인가. <b>손으로 정리하라는 말은 게임의 맞바꿈이
        /// 아직 도는 경우에만 맞다</b> - 수동 드래그도 같은 <c>Swap</c> 을 타므로, 그 경로가 깨진
        /// 상태에서는 사람을 헛되이 붙잡는다. 무너졌으면 아무 말도 하지 않는다 - 무엇을 할지는
        /// <see cref="Collapsed"/> 가 결과 끝에 한 번 붙인다.
        /// </summary>
        private string? Recovery() => Collapsed() is null ? TidyMessage : null;

        private string? InvalidState()
        {
            var error = _port.StateError;
            if (error == null) return null;
            DiagnosticError ??= error;
            RequiresResync = true;
            _stateFailure = "가방의 아이템과 효과 연결이 어긋나 추가 이동과 되돌리기를 중단했습니다. " +
                "방에 재접속하거나 게임을 다시 시작하세요. " + error;
            return _stateFailure;
        }

        /// <summary>반복자가 값을 돌려줄 자리.</summary>
        private sealed class Outcome
        {
            /// <summary>실패했으면 사람이 읽을 이유. 성공이면 null.</summary>
            public string? Failure;

            /// <summary>해낸 걸음 수.</summary>
            public int Count;

            /// <summary>되돌리기 결과처럼 결과에 덧붙일 한 마디.</summary>
            public string? Note;

            /// <summary><see cref="Advance"/>가 잡은 예외 메시지.</summary>
            public string? Fault;
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
        /// 적용이 끝난 상태의 레벨이 계산과 같은지. 어긋나면 우리가 읽지 않는 효과가 걸려 있다는
        /// 뜻이다. 배치 자체는 합법이라 되돌리지 않고 알리기만 한다.
        /// </summary>
        private string LevelDrift()
        {
            // 인벤토리가 사라졌으면 견줄 것이 없다. 걸음은 이미 반영된 뒤이므로 실패가 아니다.
            if (!_port.Alive) return "";

            try
            {
                var mismatches = 0;
                var first = "";
                foreach (var pair in _command.ExpectedCellLevels)
                {
                    var reported = _port.LevelAt(pair.Key);
                    if (reported == pair.Value) continue;

                    mismatches++;
                    if (first.Length == 0) first = $"{pair.Key} 계산 {pair.Value} != 게임 {reported}";
                }

                return mismatches == 0
                    ? ""
                    : $". 적용 뒤 레벨이 예상과 다른 칸 {mismatches}개({first}) - 계산에 없는 효과가 걸려 있습니다";
            }
            catch (Exception ex)
            {
                DiagnosticError ??= ex.ToString();
                return $". 적용 뒤 레벨을 확인하지 못했습니다({ex.Message})";
            }
        }

        private IEnumerator ApplySwaps(List<Step> journal, Outcome outcome)
        {
            var grid = new GridSpec(_command.ExpectedWidth, _command.ExpectedHeight, _command.ExpectedStorage);
            var positions = new Dictionary<int, GridPos>();
            var occupants = new Dictionary<GridPos, int>();

            // 번호가 없어 옮길 수도, 맞바꿈으로 밀어낼 수도 없는 칸. 계획은 이 칸을 비워 두지만
            // 옛 계획이나 어긋난 상태로 여기 닿을 수 있어 적용기가 한 번 더 본다.
            var immovable = new HashSet<GridPos>();
            for (var index = 0; index < grid.Storage; index++)
            {
                var cell = grid.ToPosition(index);
                var instance = _port.InstanceAt(cell);
                if (instance == 0)
                {
                    if (_port.Occupied(cell)) immovable.Add(cell);
                    continue;
                }
                positions[instance] = cell;
                occupants[cell] = instance;
            }

            var total = _command.Targets.Count;
            foreach (var target in _command.Targets)
            {
                // 못 옮기는 아이템의 목표는 언제나 제자리다. 번호가 없으니 찾을 수도 없다.
                if (target.Immovable) continue;

                if (!positions.TryGetValue(target.InstanceId, out var from))
                {
                    outcome.Failure = "계획에 있는 아이템을 가방에서 찾지 못했습니다. 자동 배치를 중단했습니다.";
                    yield break;
                }
                var to = target.To;
                if (from == to) continue;

                if (immovable.Contains(to))
                {
                    outcome.Failure = "그 칸의 아이템은 옮길 수 없어 자동 배치를 중단했습니다.";
                    yield break;
                }

                if (!_port.Alive)
                {
                    outcome.Failure = "적용 도중 인벤토리가 사라져 자동 배치를 중단했습니다.";
                    yield break;
                }

                // 적용을 시작한 뒤에 동료가 접속했을 수 있다. 멀티가 된 순간 더 진행하지 않는다.
                if (!_allowMultiplayer && _port.IsMultiplayerSession)
                {
                    outcome.Failure = "적용 도중 멀티플레이 세션이 되어 자동 배치를 중단했습니다.";
                    yield break;
                }

                if (!BudgetLeft())
                {
                    outcome.Failure = $"서버 반영이 느려 {PhaseTimeout:0}초 안에 끝내지 못해 자동 배치를 중단했습니다.";
                    yield break;
                }

                var displaced = occupants.TryGetValue(to, out var occupant);
                var expected = displaced ? occupant : 0;

                if (!Swapped(to, from, target.InstanceId, expected))
                {
                    outcome.Failure = "이동할 칸의 아이템이 바뀌어 자동 배치를 중단했습니다.";
                    yield break;
                }

                Progress = $"자동 배치 중 - 이동 {outcome.Count + 1}번째 반영 대기";
                var stateError = InvalidState();
                if (stateError != null)
                {
                    outcome.Failure = stateError;
                    yield break;
                }
                RequiresResync = !_port.WritesLandImmediately;
                var error = _port.Swap(from, to);
                stateError = InvalidState();
                if (stateError != null)
                {
                    outcome.Failure = stateError;
                    yield break;
                }
                if (error != null)
                {
                    if (_port.WritesLandImmediately && Swapped(from, to, target.InstanceId, expected))
                        journal.Add(new Step(from, to, target.InstanceId, expected));
                    outcome.Failure = $"이동 중 오류({error})가 나 자동 배치를 중단했습니다.";
                    yield break;
                }

                // 호스트의 쓰기는 그 자리에서 끝나므로 기다릴 것이 없다. 참가자 세션에서는 CmdSwap
                // 이 서버를 돌아 SyncDictionary 로 돌아올 때까지 아직 아무것도 바뀌지 않았다.
                var deadline = Deadline();
                while (_port.Alive && !Swapped(from, to, target.InstanceId, expected) && _now() < deadline)
                    yield return null;

                // 게임의 LocalSwap 은 쓰기 권한이 없거나 포션 줄이면 예외 없이 로그만 남기고
                // 돌아온다. 그런 걸음을 저널에 올리면 나중 되돌리기가 "되돌리기"가 아니라
                // "처음 적용"이 되어 원래 배치와 다른 순열을 남긴다. 실제로 옮겨졌는지 본다.
                if (!_port.Alive || !Swapped(from, to, target.InstanceId, expected))
                {
                    outcome.Failure = !_port.Alive
                        ? "적용 도중 인벤토리가 사라져 자동 배치를 중단했습니다."
                        : $"{from} → {to} 이동이 게임에 반영되지 않아 자동 배치를 중단했습니다.";
                    yield break;
                }

                RequiresResync = false;
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

        /// <summary>
        /// 그 걸음이 실제로 반영됐는가. 맞바꿈이라 밀려난 쪽까지 제자리에 와야 끝난 것이다 -
        /// 옮긴 쪽만 보면 참가자 세션에서 절반만 도착한 상태를 완료로 읽을 수 있다.
        /// </summary>
        private bool Swapped(GridPos from, GridPos to, int moved, int displaced) =>
            _port.InstanceAt(to) == moved && _port.InstanceAt(from) == displaced;

        /// <summary>
        /// 실행한 맞바꿈을 역순으로 재생해 원래 배치로 되돌린다. 맞바꿈은 자기 자신이 역연산이라
        /// 이전 상태를 따로 저장할 필요가 없다.
        /// </summary>
        private IEnumerator Rollback(List<Step> journal, Outcome outcome)
        {
            if (RequiresResync)
            {
                outcome.Note = UncertainMessage;
                yield break;
            }
            if (journal.Count == 0) yield break;

            NewPhase();
            Progress = "자동 배치 - 되돌리는 중";
            for (var i = journal.Count - 1; i >= 0; i--)
            {
                if (!_port.Alive)
                {
                    outcome.Note = "되돌리는 중 인벤토리가 사라져 중간 상태로 남았습니다.";
                    yield break;
                }

                if (!BudgetLeft())
                {
                    outcome.Note = Join(
                        $"되돌리기가 {PhaseTimeout:0}초 안에 끝나지 않아 인벤토리가 중간 상태로 남았습니다.", Recovery());
                    yield break;
                }

                var step = journal[i];
                if (!Swapped(step.From, step.To, step.InstanceId, step.Displaced))
                {
                    outcome.Note = "되돌릴 칸의 아이템이 바뀌어 추가로 옮기지 않았습니다. 현재 배치를 확인하세요.";
                    yield break;
                }
                RequiresResync = !_port.WritesLandImmediately;
                var stateError = InvalidState();
                if (stateError != null)
                {
                    outcome.Note = stateError;
                    yield break;
                }
                var error = _port.Swap(step.To, step.From);
                stateError = InvalidState();
                if (stateError != null)
                {
                    outcome.Note = stateError;
                    yield break;
                }
                if (error != null)
                {
                    outcome.Note = Join(
                        $"되돌리기도 실패해 인벤토리가 중간 상태로 남았습니다({error}).",
                        RequiresResync ? UncertainMessage : Recovery());
                    yield break;
                }

                var deadline = Deadline();
                while (_port.Alive &&
                       !Swapped(step.To, step.From, step.InstanceId, step.Displaced) &&
                       _now() < deadline)
                {
                    yield return null;
                }

                // 앞으로 가는 길과 같은 이유다. LocalSwap 은 거부해도 예외 없이 돌아오므로,
                // 확인하지 않으면 거부된 되돌리기를 "원래 배치로 되돌렸습니다"로 보고하게 된다.
                if (!_port.Alive || !Swapped(step.To, step.From, step.InstanceId, step.Displaced))
                {
                    outcome.Note = Join(
                        "되돌리기가 게임에 반영되지 않아 인벤토리가 중간 상태로 남았습니다.",
                        RequiresResync ? UncertainMessage : Recovery());
                    yield break;
                }
                RequiresResync = false;
            }
            foreach (var target in _command.Targets)
            {
                // 번호 없는 아이템은 게임이 0 을 돌려주어 우리 음수 번호와 견줄 수 없다. TargetDrift 처럼
                // 칸이 차 있는지만 본다.
                if (target.Immovable)
                {
                    if (!_port.Occupied(target.From))
                    {
                        outcome.Note = "확인된 이동은 되돌렸지만 원래 배치와 다른 항목이 남았습니다. 현재 배치를 확인하세요.";
                        yield break;
                    }
                    continue;
                }

                if (_port.InstanceAt(target.From) != target.InstanceId ||
                    target.IsTablet && RotationOf(target.InstanceId) != target.FromRotation)
                {
                    outcome.Note = "확인된 이동은 되돌렸지만 원래 배치와 다른 항목이 남았습니다. 현재 배치를 확인하세요.";
                    yield break;
                }
            }
            outcome.Note = "원래 배치로 되돌렸습니다.";
        }

        /// <summary>
        /// 회전. 호스트는 각도를 그대로 쓰는 길이 있어 한 번에 끝난다. 참가자에게는 그 길이 없다 -
        /// 우클릭이 타는 경로로 필요한 횟수만큼 누르고, 서버가 한 번에 90도씩 돌리므로 누를 때마다
        /// 반영을 기다린다. 도중에 막히면 눌러 둔 만큼 마저 눌러 원래 각도로 되돌린다.
        /// </summary>
        private IEnumerator Rotate(Outcome outcome)
        {
            var pending = new List<TabletTurn>();
            var error = Collect(pending);
            if (error != null)
            {
                outcome.Failure = error;
                yield break;
            }
            if (pending.Count == 0) yield break;

            if (_port.WritesLandImmediately)
            {
                var failure = _port.Rotate(pending);
                if (failure != null) outcome.Failure = failure;
                else outcome.Count = pending.Count;
                yield break;
            }

            var pressed = new List<TabletTurn>();
            foreach (var turn in pending)
            {
                var count = 0;
                string? failure = null;
                while (count < turn.Presses)
                {
                    var step = new Outcome();
                    var pressing = PressOnce(turn, step);
                    while (Advance(pressing, step)) yield return null;

                    var trouble = step.Fault != null ? $"회전 중 오류({step.Fault})" : step.Failure;
                    if (trouble != null)
                    {
                        failure = trouble + " - 자동 배치를 중단합니다.";
                        break;
                    }
                    count++;
                    Progress = $"자동 배치 중 - 회전 {outcome.Count + 1}/{pending.Count}";
                }

                pressed.Add(new TabletTurn(turn.InstanceId, turn.Cell, turn.Rotation, count));
                if (failure == null)
                {
                    outcome.Count++;
                    continue;
                }

                outcome.Failure = failure;
                if (RequiresResync) yield break;
                var undone = new Outcome();
                var restoring = RestoreRotations(pressed, undone);
                while (Advance(restoring, undone)) yield return null;
                outcome.Note = undone.Fault != null
                    ? $"각도를 되돌리는 중 오류가 났습니다({undone.Fault})."
                    : undone.Note;
                yield break;
            }
        }

        /// <summary>눌러 둔 만큼 마저 눌러 한 바퀴를 채운다. 회전이 4 걸음이라 그것이 역연산이다.</summary>
        private IEnumerator RestoreRotations(List<TabletTurn> pressed, Outcome outcome)
        {
            NewPhase();
            Progress = "자동 배치 - 각도를 되돌리는 중";
            foreach (var turn in pressed)
            {
                var back = TabletRotation.PressesBack(turn.Presses);
                var original = _command.Targets.Find(target => target.InstanceId == turn.InstanceId);
                if (original == null || RotationOf(turn.InstanceId) !=
                    (original.FromRotation + turn.Presses) % TabletRotation.Steps)
                {
                    outcome.Note = "각도를 되돌리기 전에 석판 상태가 바뀌어 멈췄습니다. 현재 배치를 확인하세요.";
                    yield break;
                }
                for (var i = 0; i < back; i++)
                {
                    var step = new Outcome();
                    var pressing = PressOnce(turn, step);
                    while (Advance(pressing, step)) yield return null;

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
        /// 직전에 그 칸이 아직 이 석판의 것인지 본다. 석판 자신의 좌표 필드는 우리가 걸음마다
        /// 기다리는 격자와 별개로 동기화되므로, 믿고 누르면 엉뚱한 칸을 우클릭할 수 있고 그것은
        /// 되돌릴 방법도 알 방법도 없다.
        /// </summary>
        private IEnumerator PressOnce(TabletTurn turn, Outcome outcome)
        {
            var before = RotationOf(turn.InstanceId);
            if (before is null)
            {
                outcome.Failure = $"석판(인스턴스 {turn.InstanceId})이 사라졌습니다";
                yield break;
            }
            if (_port.InstanceAt(turn.Cell) != turn.InstanceId)
            {
                outcome.Failure = $"석판(인스턴스 {turn.InstanceId})이 {turn.Cell} 에 없습니다";
                yield break;
            }
            if (!BudgetLeft())
            {
                outcome.Failure = $"서버 반영이 느려 {PhaseTimeout:0}초 안에 끝내지 못했습니다";
                yield break;
            }

            Progress = "자동 배치 중 - 회전 반영 대기";
            RequiresResync = !_port.WritesLandImmediately;
            var error = _port.Press(turn.Cell);
            if (error != null)
            {
                outcome.Failure = $"회전 중 오류({error})";
                yield break;
            }

            var deadline = Deadline();
            while (RotationOf(turn.InstanceId) == before && _now() < deadline)
                yield return null;

            if (RotationOf(turn.InstanceId) is not int after || after == before)
                outcome.Failure = $"석판(인스턴스 {turn.InstanceId}) 회전이 시간 안에 반영되지 않았습니다";
            else if (after != (before.Value + 1) % TabletRotation.Steps)
            {
                RequiresResync = true;
                outcome.Failure = $"석판(인스턴스 {turn.InstanceId})이 예상과 다른 각도로 바뀌었습니다";
            }
            else RequiresResync = false;
        }

        /// <summary>석판의 지금 각도. 사라졌으면 null.</summary>
        private int? RotationOf(int instanceId) =>
            _port.Alive && _port.TryFindTablet(instanceId, out _, out var rotation, out _)
                ? rotation
                : (int?)null;

        /// <summary>
        /// 돌려야 할 석판과 횟수를 모은다. 솔버도 스냅샷의 인스턴스별 회전 가능 여부를 보지만,
        /// 계산 이후 저주 등으로 잠겼을 수 있어 쓰기 직전에 한 번 더 확인한다.
        /// </summary>
        private string? Collect(List<TabletTurn> pending)
        {
            try
            {
                foreach (var target in _command.Targets)
                {
                    if (!target.IsTablet) continue;

                    if (!_port.Alive ||
                        !_port.TryFindTablet(target.InstanceId, out var cell, out var rotation, out var rotatable))
                        return "적용 도중 석판이 사라져 자동 배치를 중단합니다.";

                    var presses = TabletRotation.PressesFrom(rotation, target.Rotation);
                    if (presses == 0) continue;

                    if (!rotatable)
                        return $"적용 도중 석판(인스턴스 {target.InstanceId})의 회전이 잠겨 중단합니다.";

                    pending.Add(new TabletTurn(target.InstanceId, cell, target.Rotation, presses));
                }
                return null;
            }
            catch (Exception ex)
            {
                DiagnosticError ??= ex.ToString();
                // 아직 아무것도 쓰지 않았다. 회전만 포기하면 이동을 되돌릴지 호출자가 정한다.
                return $"회전 준비 중 오류가 났습니다({ex.Message}).";
            }
        }
    }
}
