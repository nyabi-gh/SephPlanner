using System.Collections;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 남의 인벤토리를 만지는 순서다. 게임 없이 가짜 격자 위에서 걸음 순서·저널·되돌리기·시간 상한을
/// 시험한다. 게임이 조용히 거부한 걸음을 성공으로 보고하거나, 반쯤 적용된 배치를 남기고 "되돌렸다"고
/// 하는 것이 이 코드가 저지를 수 있는 가장 나쁜 일이다.
/// </summary>
public class ApplyPlanRoutineTests
{
    /// <summary>
    /// 가짜 인벤토리. 호스트 모드에서는 쓰기가 그 자리에서 반영되고, 참가자 모드에서는 지연 뒤에
    /// 반영된다. 거부·오류·소멸을 걸음 단위로 심을 수 있다.
    /// </summary>
    private sealed class FakeInventory : IInventoryPort
    {
        public readonly Dictionary<GridPos, int> Cells = new();
        public readonly Dictionary<int, (int Rotation, bool Rotatable)> Tablets = new();
        public readonly Dictionary<GridPos, int> Levels = new();
        public readonly List<string> Log = new();

        public bool Alive { get; set; } = true;
        public bool WritesLandImmediately { get; set; } = true;
        public bool IsMultiplayerSession { get; set; }
        public string? StateError { get; set; }

        /// <summary>참가자 모드에서 쓰기가 반영되기까지 걸리는 시간(초).</summary>
        public double Latency { get; set; }
        public int PressIncrement { get; set; } = 1;
        public bool IgnoreHostRotation { get; set; }
        public Action<int>? BeforeSwap { get; set; }
        public Action<int>? AfterSwap { get; set; }
        public HashSet<int> ErrorsAfterSwap { get; } = new();

        /// <summary>몇 번째 맞바꿈(0부터)을 게임이 조용히 거부하는지. 오류 없이 돌아오고 아무것도 안 바뀐다.</summary>
        public HashSet<int> RejectedSwaps { get; } = new();

        /// <summary>몇 번째 맞바꿈이 예외 대신 오류 문자열을 돌려주는지.</summary>
        public HashSet<int> FailingSwaps { get; } = new();

        /// <summary>몇 번째 누르기(0부터)를 서버가 조용히 무시하는지.</summary>
        public HashSet<int> IgnoredPresses { get; } = new();

        /// <summary>호스트 회전이 돌려줄 오류. null 이면 성공.</summary>
        public string? RotateError { get; set; }

        /// <summary>다음 읽기 하나가 예외를 던지게 한다(파괴된 오브젝트 흉내). 던지고 나면 풀린다.</summary>
        public bool ThrowOnNextRead { get; set; }

        public Func<double> Now { get; set; } = () => 0;

        private readonly List<(double At, Action Apply)> _pending = new();
        private int _swaps;
        private int _presses;

        public int SwapCount => _swaps;
        public int PressCount => _presses;

        public int InstanceAt(GridPos cell)
        {
            if (ThrowOnNextRead)
            {
                ThrowOnNextRead = false;
                throw new InvalidOperationException("destroyed");
            }
            return Cells.TryGetValue(cell, out var id) ? id : 0;
        }

        /// <summary>번호 없는 아이템이 앉은 칸. <see cref="InstanceAt"/> 는 0 을 돌려준다.</summary>
        public readonly HashSet<GridPos> Unnumbered = new();

        public bool Occupied(GridPos cell) => Cells.ContainsKey(cell) || Unnumbered.Contains(cell);

        public string? Swap(GridPos from, GridPos to)
        {
            var index = _swaps++;
            BeforeSwap?.Invoke(index);
            Log.Add($"swap {from}->{to}");
            if (FailingSwaps.Contains(index)) return "권한 없음";
            if (RejectedSwaps.Contains(index)) return null;

            Schedule(() =>
            {
                Cells.TryGetValue(from, out var a);
                Cells.TryGetValue(to, out var b);
                Put(to, a);
                Put(from, b);
                AfterSwap?.Invoke(index);
            });
            return ErrorsAfterSwap.Contains(index) ? "교환 후 오류" : null;
        }

        public bool TryFindTablet(int instanceId, out GridPos cell, out int rotation, out bool rotatable)
        {
            if (ThrowOnNextRead)
            {
                ThrowOnNextRead = false;
                throw new InvalidOperationException("destroyed");
            }
            foreach (var pair in Cells)
            {
                if (pair.Value != instanceId || !Tablets.TryGetValue(instanceId, out var tablet)) continue;
                cell = pair.Key;
                rotation = tablet.Rotation;
                rotatable = tablet.Rotatable;
                return true;
            }
            cell = default;
            rotation = 0;
            rotatable = false;
            return false;
        }

        public string? Press(GridPos cell)
        {
            var index = _presses++;
            Log.Add($"press {cell}");
            if (IgnoredPresses.Contains(index)) return null;

            Schedule(() =>
            {
                var id = InstanceAt(cell);
                if (!Tablets.TryGetValue(id, out var tablet)) return;
                Tablets[id] = ((tablet.Rotation + PressIncrement) % 4, tablet.Rotatable);
            });
            return null;
        }

        public string? Rotate(IReadOnlyList<TabletTurn> turns)
        {
            Log.Add($"rotate {turns.Count}");
            if (RotateError != null) return RotateError;
            if (IgnoreHostRotation) return null;
            foreach (var turn in turns)
                Tablets[turn.InstanceId] = (turn.Rotation, Tablets[turn.InstanceId].Rotatable);
            return null;
        }

        public int LevelAt(GridPos cell) => Levels.TryGetValue(cell, out var level) ? level : 0;

        /// <summary>시계가 흐른 만큼 반영된 쓰기를 적용한다.</summary>
        public void Settle()
        {
            var now = Now();
            var due = _pending.Where(p => p.At <= now).ToList();
            _pending.RemoveAll(p => p.At <= now);
            foreach (var entry in due) entry.Apply();
        }

        private void Schedule(Action apply)
        {
            if (WritesLandImmediately) apply();
            else _pending.Add((Now() + Latency, apply));
        }

        private void Put(GridPos cell, int id)
        {
            if (id == 0) Cells.Remove(cell);
            else Cells[cell] = id;
        }
    }

    private sealed class Clock
    {
        public double Now;
    }

    private const int Width = 6;
    private const int Height = 7;
    private const int Storage = 12;

    private static ApplyPlanCommand Command(params PlanTarget[] targets) => new()
    {
        ExpectedWidth = Width,
        ExpectedHeight = Height,
        ExpectedStorage = Storage,
        Targets = targets.ToList(),
    };

    private static PlanTarget Charm(int id, GridPos from, GridPos to) => new()
    {
        InstanceId = id,
        From = from,
        To = to,
    };

    private static PlanTarget Tablet(int id, GridPos from, GridPos to, int fromRotation, int rotation) => new()
    {
        InstanceId = id,
        From = from,
        To = to,
        IsTablet = true,
        FromRotation = fromRotation,
        Rotation = rotation,
    };

    private static GridPos At(int x, int y) => new(x, y);

    private static void AssertContains(string expected, string actual) =>
        Assert.True(actual.Contains(expected, StringComparison.Ordinal), $"\"{expected}\" 가 없다: {actual}");

    /// <summary>
    /// 반복자를 끝까지 돌린다. 멈출 때마다 시계를 조금 흘리고 반영된 쓰기를 적용한다 - 프레임이
    /// 지나가는 것과 같다. 돌린 프레임 수를 돌려준다.
    /// </summary>
    private static int Drive(ApplyPlanRoutine routine, FakeInventory inventory, Clock clock, double frame = 0.1)
    {
        var run = routine.Run();
        var frames = 0;
        while (run.MoveNext())
        {
            frames++;
            clock.Now += frame;
            inventory.Settle();
            Assert.True(frames < 100_000, "반복자가 끝나지 않는다");
        }
        return frames;
    }

    /// <summary>
    /// 번호 없는 아이템(시나리오 동행 증표)은 제자리가 목표다. 번호로 찾으려 들면 가방에서
    /// 못 찾아 그 자리에서 중단하고, 나머지 이동까지 막힌다 - 제보 <c>6961d1a0</c> 이 그랬다.
    /// </summary>
    [Fact]
    public void AnImmovableTargetIsSkippedAndTheRestStillApplies()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Unnumbered.Add(At(2, 1));

        var routine = Routine(
            Command(
                new PlanTarget { InstanceId = -9, From = At(2, 1), To = At(2, 1), Immovable = true },
                Charm(10, At(0, 0), At(1, 0))),
            inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(10, inventory.InstanceAt(At(1, 0)));
        Assert.True(routine.Settled);
        AssertContains("자동 배치 완료", routine.Result);
    }

    /// <summary>
    /// 번호 없는 칸은 <c>InstanceAt</c> 이 0 을 돌려주어 빈 칸처럼 보인다. 거기로 밀어 넣으면
    /// 게임 안에서 무슨 일이 나는지 모르므로 옮기기 전에 멈춘다.
    /// </summary>
    [Fact]
    public void MovingIntoAnUnnumberedCellStopsBeforeTheWrite()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Unnumbered.Add(At(1, 0));

        var routine = Routine(Command(Charm(10, At(0, 0), At(1, 0))), inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(0, inventory.SwapCount);
        Assert.False(routine.Settled);
        AssertContains("옮길 수 없어", routine.Result);
    }

    private static (FakeInventory Inventory, Clock Clock) Host()
    {
        var clock = new Clock();
        var inventory = new FakeInventory { Now = () => clock.Now };
        return (inventory, clock);
    }

    private static (FakeInventory Inventory, Clock Clock) Guest(double latency = 0.5)
    {
        var (inventory, clock) = Host();
        inventory.WritesLandImmediately = false;
        inventory.Latency = latency;
        return (inventory, clock);
    }

    private static ApplyPlanRoutine Routine(
        ApplyPlanCommand command, FakeInventory inventory, Clock clock, bool allowMultiplayer = false) =>
        new(command, inventory, () => clock.Now, allowMultiplayer);

    [Fact]
    public void ADelayedWriteKeepsRecoveryRequiredEvenAfterItEventuallyLands()
    {
        var (inventory, clock) = Guest(latency: 4);
        inventory.Cells[At(0, 0)] = 10;
        var routine = Routine(Command(Charm(10, At(0, 0), At(1, 0))), inventory, clock);
        Drive(routine, inventory, clock);
        Assert.True(routine.RequiresResync);
        Assert.Equal(10, inventory.InstanceAt(At(0, 0)));
        clock.Now = 5;
        inventory.Settle();
        Assert.Equal(10, inventory.InstanceAt(At(1, 0)));
        Assert.True(routine.RequiresResync);
        Assert.Equal(1, inventory.SwapCount);
        AssertContains("늦게 적용될 수 있어", routine.Result);
    }

    [Fact]
    public void AGuestNeverTreatsAnUnexpectedAngleAsSuccess()
    {
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 1;
        inventory.Tablets[1] = (0, true);
        inventory.PressIncrement = 2;
        var routine = Routine(Command(Tablet(1, At(0, 0), At(0, 0), 0, 1)), inventory, clock);
        Drive(routine, inventory, clock);
        Assert.Equal(2, inventory.Tablets[1].Rotation);
        Assert.True(routine.RequiresResync);
        Assert.Equal(1, inventory.PressCount);
        Assert.DoesNotContain("자동 배치 완료", routine.Result);
    }

    [Fact]
    public void AHostMustAlsoReachTheTargetAngle()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 1;
        inventory.Tablets[1] = (0, true);
        inventory.IgnoreHostRotation = true;
        var routine = Routine(Command(Tablet(1, At(0, 0), At(0, 0), 0, 1)), inventory, clock);
        Drive(routine, inventory, clock);
        AssertContains("최종 각도가 목표와 다릅니다", routine.Result);
        Assert.DoesNotContain("자동 배치 완료", routine.Result);
    }

    [Fact]
    public void TheNextSwapDoesNotTouchAnExternallyReplacedItem()
    {
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        var routine = Routine(Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0))), inventory, clock);
        var run = routine.Run();
        Assert.True(run.MoveNext());
        inventory.Cells[At(1, 0)] = 99;
        inventory.Cells[At(4, 0)] = 11;
        while (run.MoveNext()) { clock.Now += 0.1; inventory.Settle(); }
        Assert.Equal(99, inventory.InstanceAt(At(1, 0)));
        Assert.Equal(11, inventory.InstanceAt(At(4, 0)));
        Assert.DoesNotContain("swap (1,0)->(3,0)", inventory.Log);
        AssertContains("이동할 칸의 아이템이 바뀌어", routine.Result);
        Assert.DoesNotContain("원래 배치로 되돌렸습니다", routine.Result);
    }

    [Fact]
    public void RollbackDoesNotSwapAnExternallyChangedCell()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.FailingSwaps.Add(1);
        inventory.BeforeSwap = index => { if (index == 1) inventory.Cells[At(2, 0)] = 99; };
        var routine = Routine(Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0))), inventory, clock);
        Drive(routine, inventory, clock);
        Assert.Equal(99, inventory.InstanceAt(At(2, 0)));
        Assert.Equal(2, inventory.SwapCount);
        AssertContains("되돌릴 칸의 아이템이 바뀌어", routine.Result);
    }

    [Fact]
    public void FinalPositionsAreCheckedAgainAfterLevelSynchronization()
    {
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 10;
        var command = Command(Charm(10, At(0, 0), At(1, 0)));
        command.ExpectedCellLevels[At(1, 0)] = 1;
        var routine = Routine(command, inventory, clock);
        var run = routine.Run();
        while (run.MoveNext())
        {
            clock.Now += 0.1;
            inventory.Settle();
            if (routine.Progress == "자동 배치 - 레벨 확인 중") inventory.Cells[At(1, 0)] = 99;
        }
        AssertContains("목표 배치와 다릅니다", routine.Result);
        Assert.DoesNotContain("자동 배치 완료", routine.Result);
    }

    [Fact]
    public void TheFirstPendingWriteAlreadyHasProgress()
    {
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 10;
        var routine = Routine(Command(Charm(10, At(0, 0), At(1, 0))), inventory, clock);
        var run = routine.Run();
        Assert.True(run.MoveNext());
        AssertContains("반영 대기", routine.Progress);
    }

    [Fact]
    public void AHostApplyFinishesInsideOneFrame()
    {
        // 호스트의 쓰기는 그 자리에서 끝난다. 프레임이 끼면 그 사이에 게임 상태가 바뀔 수 있어
        // 원자적이라는 전제가 무너지므로, 한 번도 멈추지 않아야 한다.
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        var command = Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(0, 0)));

        var routine = Routine(command, inventory, clock);
        var frames = Drive(routine, inventory, clock);

        Assert.Equal(0, frames);
        Assert.Equal(10, inventory.Cells[At(2, 0)]);
        Assert.Equal(11, inventory.Cells[At(0, 0)]);
        Assert.False(inventory.Cells.ContainsKey(At(1, 0)));
        Assert.Equal("자동 배치 완료 - 이동 2건, 회전 0건", routine.Result);
    }

    [Fact]
    public void AGuestWaitsForEachStepToLandBeforeTheNext()
    {
        // 참가자의 걸음은 서버를 돌아온다. 반영을 기다리지 않고 쏘기만 하면 두 번째 맞바꿈이
        // 아직 옛 자리를 보고 엉뚱한 것을 옮긴다.
        var (inventory, clock) = Guest(latency: 0.5);
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        var command = Command(Charm(10, At(0, 0), At(1, 0)), Charm(11, At(1, 0), At(2, 0)));

        var routine = Routine(command, inventory, clock);
        var frames = Drive(routine, inventory, clock);

        Assert.True(frames > 0);
        Assert.Equal(10, inventory.Cells[At(1, 0)]);
        Assert.Equal(11, inventory.Cells[At(2, 0)]);
        // 첫 맞바꿈이 11 을 (0,0) 으로 밀어냈으므로 두 번째는 거기서 출발해야 한다.
        Assert.Equal(
            new[] { "swap (0,0)->(1,0)", "swap (0,0)->(2,0)" },
            inventory.Log);
        Assert.Equal("자동 배치 완료 - 이동 2건, 회전 0건", routine.Result);
    }

    [Fact]
    public void AGuestCannotDistinguishARejectedStepFromADelayedOne()
    {
        // 참가자는 무응답의 원인을 알 수 없다. 지연된 명령과 되돌리기를 교차시키지 않는다.
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.RejectedSwaps.Add(1);
        var command = Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0)));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(10, inventory.Cells[At(2, 0)]);
        Assert.Equal(11, inventory.Cells[At(1, 0)]);
        Assert.Equal(2, inventory.Cells.Count);
        AssertContains("(1,0) → (3,0) 이동이 게임에 반영되지 않아", routine.Result);
        AssertContains("방에 재접속", routine.Result);
        Assert.True(routine.RequiresResync);
        Assert.Equal(2, inventory.SwapCount);
    }

    [Fact]
    public void ARollbackThatDoesNotLandSaysSoInsteadOfClaimingSuccess()
    {
        // 되돌리기도 거부될 수 있다. 확인하지 않으면 "원래 배치로 되돌렸습니다"라고 하면서
        // 반쯤 적용된 배치를 남긴다.
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.RejectedSwaps.Add(1);
        inventory.RejectedSwaps.Add(2);
        var command = Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0)));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(10, inventory.Cells[At(2, 0)]);
        AssertContains("되돌리기가 게임에 반영되지 않아 인벤토리가 중간 상태로 남았습니다", routine.Result);
        Assert.DoesNotContain("원래 배치로 되돌렸습니다", routine.Result);
    }

    [Fact]
    public void AnErroringSwapStopsAndRollsBack()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.FailingSwaps.Add(1);
        var command = Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0)));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(10, inventory.Cells[At(0, 0)]);
        Assert.Equal(11, inventory.Cells[At(1, 0)]);
        AssertContains("이동 중 오류(권한 없음)", routine.Result);
        AssertContains("원래 배치로 되돌렸습니다.", routine.Result);
    }

    /// <summary>
    /// 번호 없는 아이템은 게임이 번호 0 을 돌려주어 우리 음수 번호와 견줄 수 없다. 되돌리기 확인이
    /// 그것을 번호로 보면 다 되돌리고도 "원래 배치와 다른 항목이 남았다" 고 한다.
    /// </summary>
    [Fact]
    public void AnImmovableItemDoesNotSpoilAFullRollback()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.Unnumbered.Add(At(2, 1));
        inventory.FailingSwaps.Add(1);
        var command = Command(
            new PlanTarget { InstanceId = -9, From = At(2, 1), To = At(2, 1), Immovable = true },
            Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0)));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(10, inventory.Cells[At(0, 0)]);
        Assert.Equal(11, inventory.Cells[At(1, 0)]);
        AssertContains("원래 배치로 되돌렸습니다.", routine.Result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AnExpectedZeroLevelLayoutSettlesAfterMoving(int levelBefore)
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Levels[At(1, 0)] = levelBefore;
        inventory.BeforeSwap = _ => inventory.Levels.Clear();
        var command = Command(Charm(10, At(0, 0), At(2, 0)));
        var grid = new GridSpec(Width, Height, Storage);
        for (var index = 0; index < grid.Storage; index++)
            command.ExpectedCellLevels[grid.ToPosition(index)] = 0;

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.True(routine.Settled);
        Assert.Equal(10, inventory.Cells[At(2, 0)]);
        Assert.Equal("자동 배치 완료 - 이동 1건, 회전 0건", routine.Result);
        Assert.Null(routine.DiagnosticError);
        Assert.False(routine.RequiresResync);
    }

    [Fact]
    public void UnexpectedZeroLevelsStillReportCollapseAfterMoving()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Levels[At(1, 0)] = 2;
        inventory.BeforeSwap = _ => inventory.Levels.Clear();
        var command = Command(Charm(10, At(0, 0), At(2, 0)));
        command.ExpectedCellLevels[At(1, 0)] = 2;

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.False(routine.Settled);
        Assert.Equal(10, inventory.Cells[At(2, 0)]);
        AssertContains("칸 레벨이 전부 0", routine.Result);
        Assert.Contains("칸 레벨이 전부 0", routine.DiagnosticError!);
        Assert.DoesNotContain("계산에 없는 효과", routine.Result);
    }

    /// <summary>
    /// 게임은 맞바꿈 끝에 레벨 행렬을 다시 만든다. 그것이 예외로 멈추면 있던 레벨이 전부 0 으로
    /// 남고, 그 상태에서는 수동 드래그도 같은 경로를 타므로 "손으로 정리하라"가 사람을 헛되이
    /// 붙잡는다. 제보 5915982c 에서 맞바꿈과 되돌리기가 같은 예외로 끝난 자리다.
    /// </summary>
    [Fact]
    public void ACollapsedLevelMatrixTellsThePlayerToReloadInsteadOfTidyingUp()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.Levels[At(0, 0)] = 2;
        inventory.FailingSwaps.Add(1);
        inventory.FailingSwaps.Add(2);
        inventory.BeforeSwap = index => { if (index == 1) inventory.Levels.Clear(); };

        var routine = Routine(Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0))), inventory, clock);
        Drive(routine, inventory, clock);

        AssertContains("되돌리기도 실패해", routine.Result);
        AssertContains("칸 레벨이 전부 0", routine.Result);
        Assert.DoesNotContain("손으로 정리한 뒤", routine.Result);
    }

    [Fact]
    public void LevelsThatSurviveAFailedSwapStillAskForATidyUp()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.Levels[At(0, 0)] = 2;
        inventory.FailingSwaps.Add(1);
        inventory.FailingSwaps.Add(2);

        var routine = Routine(Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0))), inventory, clock);
        Drive(routine, inventory, clock);

        AssertContains("되돌리기도 실패해", routine.Result);
        AssertContains("손으로 정리한 뒤", routine.Result);
        Assert.DoesNotContain("칸 레벨이 전부 0", routine.Result);
    }

    [Fact]
    public void BrokenEffectLinksStopFurtherWritesEvenWhenEnchantmentLevelsRemain()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.Levels[At(1, 0)] = 1;
        inventory.AfterSwap = _ => inventory.StateError = "모래시계 효과 연결 불일치";
        inventory.ErrorsAfterSwap.Add(0);

        var routine = Routine(Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0))), inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(1, inventory.SwapCount);
        Assert.True(routine.RequiresResync);
        Assert.False(routine.Settled);
        Assert.Contains("효과 연결", routine.Result);
        Assert.Contains("재접속", routine.Result);
        Assert.DoesNotContain("손으로 정리", routine.Result);
    }

    [Fact]
    public void AnAlreadyBrokenEffectLinkIsRejectedBeforeAnyWrite()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.StateError = "효과 객체 누락";
        var routine = Routine(Command(Charm(10, At(0, 0), At(2, 0))), inventory, clock);

        Drive(routine, inventory, clock);

        Assert.Equal(0, inventory.SwapCount);
        Assert.True(routine.RequiresResync);
        Assert.Contains("재접속", routine.Result);
    }

    [Fact]
    public void ACompletedSwapThatReportsAnErrorIsIncludedInRollback()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.ErrorsAfterSwap.Add(1);
        var routine = Routine(Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0))), inventory, clock);

        Drive(routine, inventory, clock);

        Assert.Equal(10, inventory.Cells[At(0, 0)]);
        Assert.Equal(11, inventory.Cells[At(1, 0)]);
        Assert.Equal(4, inventory.SwapCount);
        Assert.False(routine.RequiresResync);
        Assert.False(routine.Settled);
    }

    /// <summary>자리는 돌아왔어도 효과가 죽어 있으면 "되돌렸다" 만으로는 사실이 아니다.</summary>
    [Fact]
    public void ARollbackThatRestoredThePositionsStillReportsDeadEffects()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        inventory.Levels[At(0, 0)] = 2;
        inventory.FailingSwaps.Add(1);
        inventory.BeforeSwap = index => { if (index == 1) inventory.Levels.Clear(); };

        var routine = Routine(Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0))), inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(10, inventory.Cells[At(0, 0)]);
        AssertContains("원래 배치로 되돌렸습니다.", routine.Result);
        AssertContains("칸 레벨이 전부 0", routine.Result);
    }

    [Fact]
    public void AReadFailureAfterSendingDoesNotGuessWhetherTheWriteLanded()
    {
        // 참가자 세션에서는 걸음 사이가 수 초라 그동안 인벤토리가 파괴될 수 있다. 읽기가 던지는
        // 예외를 놓치면 이미 옮긴 걸음이 되돌려지지 않은 채 아무 말도 없다.
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        // 두 번째 걸음은 서버가 받지 않는다. 적용기는 이를 알 수 없으므로 읽기 실패 뒤에도
        // 미확정 쓰기를 잊지 않고 추가 이동을 막아야 한다.
        inventory.RejectedSwaps.Add(1);
        var command = Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0)));

        var routine = Routine(command, inventory, clock);
        var run = routine.Run();
        var frames = 0;
        while (run.MoveNext())
        {
            clock.Now += 0.1;
            inventory.Settle();
            // 첫 걸음이 반영된 직후 읽기 하나가 터진다. 두 번째 걸음의 확인에서 잡힌다.
            if (++frames == 6) inventory.ThrowOnNextRead = true;
        }

        AssertContains("적용 도중 오류가 나 자동 배치를 중단했습니다(destroyed)", routine.Result);
        AssertContains("방에 재접속", routine.Result);
        Assert.True(routine.RequiresResync);
        Assert.Equal(10, inventory.Cells[At(2, 0)]);
        Assert.Equal(11, inventory.Cells[At(1, 0)]);
    }

    [Fact]
    public void TheForwardPhaseHasAnOverallCapAndTheRollbackGetsItsOwn()
    {
        // 걸음마다 3초만 보면 전체 상한이 없어, 서버가 느리면 걸음 수 × 3초 동안 인벤토리가 저 혼자
        // 움직인다. 앞으로 가는 국면은 30초에서 멈추고, 되돌리기는 제 몫을 새로 받아 끝까지 간다.
        var (inventory, clock) = Guest(latency: 2.5);
        var targets = new List<PlanTarget>();
        for (var i = 0; i < 14; i++)
        {
            inventory.Cells[At(i % Width, i / Width)] = 10 + i;
            targets.Add(Charm(10 + i, At(i % Width, i / Width), At(i % Width, 3 + i / Width)));
        }
        var command = Command(targets.ToArray());
        command.ExpectedStorage = Width * Height;

        var routine = Routine(command, inventory, clock);
        var run = routine.Run();
        while (run.MoveNext())
        {
            clock.Now += 0.5;
            inventory.Settle();
            // 되돌리기는 빨리 반영되게 해, 앞 국면의 상한만 재도록 한다.
            if (routine.Progress == "자동 배치 - 되돌리는 중") inventory.Latency = 0.5;
        }

        AssertContains("30초 안에 끝내지 못해", routine.Result);
        AssertContains("원래 배치로 되돌렸습니다.", routine.Result);
        // 2.5초짜리 걸음 열둘이 30초를 채우고, 열셋째는 보내지 않는다. 되돌리기도 열둘이다.
        Assert.Equal(24, inventory.SwapCount);
        for (var i = 0; i < 14; i++) Assert.Equal(10 + i, inventory.Cells[At(i % Width, i / Width)]);

        // 되돌리기가 새 국면을 받았다는 증거 - 앞이 30초 안에 멈췄는데도 되돌린 걸음 전부가 반영됐다.
        Assert.True(clock.Now > ApplyPlanRoutine.PhaseTimeout, $"되돌리기까지 {clock.Now}초");
        Assert.True(routine.AbandonAt > ApplyPlanRoutine.PhaseTimeout + ApplyPlanRoutine.SyncTimeout);
    }

    [Fact]
    public void AGuestRotatesByPressingTheCellTheRightNumberOfTimes()
    {
        // 참가자에게는 각도를 쓰는 길이 없다. 우클릭 경로로 한 걸음씩 누르고, 한 번에 90도씩
        // 돌아오는 것을 확인한 뒤 다음을 누른다.
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 1;
        inventory.Tablets[1] = (1, true);
        var command = Command(Tablet(1, At(0, 0), At(0, 0), fromRotation: 1, rotation: 0));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(3, inventory.PressCount);
        Assert.Equal(0, inventory.Tablets[1].Rotation);
        Assert.Equal("자동 배치 완료 - 이동 0건, 회전 1건", routine.Result);
    }

    [Fact]
    public void AHostRotatesEverythingAtOnceWithTheTargetAngles()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 1;
        inventory.Cells[At(1, 0)] = 2;
        inventory.Tablets[1] = (0, true);
        inventory.Tablets[2] = (3, true);
        var command = Command(
            Tablet(1, At(0, 0), At(0, 0), fromRotation: 0, rotation: 2),
            Tablet(2, At(1, 0), At(1, 0), fromRotation: 3, rotation: 3));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(new[] { "rotate 1" }, inventory.Log);
        Assert.Equal(2, inventory.Tablets[1].Rotation);
        Assert.Equal(3, inventory.Tablets[2].Rotation);
        Assert.Equal("자동 배치 완료 - 이동 0건, 회전 1건", routine.Result);
    }

    [Fact]
    public void AFailedRotationUndoesTheMovesToo()
    {
        // 옮겨졌지만 안 돌아간 배치는 솔버가 평가한 적 없는 상태다. 회전이 실패하면 이동도 되돌린다.
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 1;
        inventory.Cells[At(2, 0)] = 10;
        inventory.Tablets[1] = (0, true);
        inventory.RotateError = "회전 중 오류가 나 원래 각도로 되돌렸습니다(테스트).";
        var command = Command(
            Charm(10, At(2, 0), At(3, 0)),
            Tablet(1, At(0, 0), At(1, 0), fromRotation: 0, rotation: 1));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(1, inventory.Cells[At(0, 0)]);
        Assert.Equal(10, inventory.Cells[At(2, 0)]);
        AssertContains("회전 중 오류가 나 원래 각도로 되돌렸습니다(테스트).", routine.Result);
        AssertContains("원래 배치로 되돌렸습니다.", routine.Result);
    }

    [Fact]
    public void AnUnconfirmedPressStopsWithoutSendingCompensatingPresses()
    {
        // 무시된 것인지 늦게 도착할 것인지 모르는 누르기 뒤에는 추가 회전을 보내지 않는다.
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 1;
        inventory.Cells[At(2, 0)] = 10;
        inventory.Tablets[1] = (0, true);
        inventory.IgnoredPresses.Add(1);
        var command = Command(
            Charm(10, At(2, 0), At(3, 0)),
            Tablet(1, At(0, 0), At(0, 0), fromRotation: 0, rotation: 2));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(1, inventory.Tablets[1].Rotation);
        Assert.Equal(10, inventory.Cells[At(3, 0)]);
        Assert.Equal(2, inventory.PressCount);
        AssertContains("회전이 시간 안에 반영되지 않았습니다", routine.Result);
        Assert.True(routine.RequiresResync);
        AssertContains("방에 재접속", routine.Result);
    }

    [Fact]
    public void APressGoesToTheCellOnlyWhileTheTabletIsStillThere()
    {
        // 우클릭은 칸을 누른다. 석판이 그 사이 다른 칸으로 갔으면 엉뚱한 것을 돌리게 되므로
        // 누르지 않는다.
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 1;
        inventory.Tablets[1] = (0, true);
        var command = Command(Tablet(1, At(0, 0), At(0, 0), fromRotation: 0, rotation: 2));

        var routine = Routine(command, inventory, clock);
        var run = routine.Run();
        while (run.MoveNext())
        {
            clock.Now += 0.1;
            inventory.Settle();
            // 첫 누르기가 반영된 뒤 석판이 다른 칸으로 간다. 두 번째는 눌러선 안 된다.
            if (inventory.Tablets[1].Rotation == 1 && inventory.Cells.ContainsKey(At(0, 0)))
            {
                inventory.Cells.Remove(At(0, 0));
                inventory.Cells[At(5, 0)] = 1;
            }
        }

        Assert.Equal(1, inventory.PressCount);
        AssertContains("(0,0) 에 없습니다", routine.Result);
        AssertContains("각도를 되돌리지 못했습니다", routine.Result);
    }

    [Fact]
    public void ALockedTabletStopsBeforeAnythingIsWritten()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 1;
        inventory.Tablets[1] = (0, false);
        var command = Command(Tablet(1, At(0, 0), At(0, 0), fromRotation: 0, rotation: 1));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Empty(inventory.Log);
        AssertContains("회전이 잠겨 중단합니다", routine.Result);
    }

    [Fact]
    public void ACompanionJoiningMidwayStopsTheApplyWhenMultiplayerIsNotAllowed()
    {
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        var command = Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0)));

        var routine = Routine(command, inventory, clock, allowMultiplayer: false);
        var run = routine.Run();
        var frames = 0;
        while (run.MoveNext())
        {
            clock.Now += 0.1;
            inventory.Settle();
            if (++frames == 3) inventory.IsMultiplayerSession = true;
        }

        AssertContains("멀티플레이 세션이 되어", routine.Result);
        Assert.Equal(10, inventory.Cells[At(0, 0)]);
        Assert.Equal(11, inventory.Cells[At(1, 0)]);
    }

    [Fact]
    public void AnInventoryThatVanishesMidwayIsReportedNotHidden()
    {
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Cells[At(1, 0)] = 11;
        var command = Command(Charm(10, At(0, 0), At(2, 0)), Charm(11, At(1, 0), At(3, 0)));

        var routine = Routine(command, inventory, clock);
        var run = routine.Run();
        var frames = 0;
        while (run.MoveNext())
        {
            clock.Now += 0.1;
            inventory.Settle();
            if (++frames == 6) inventory.Alive = false;
        }

        AssertContains("인벤토리가 사라져", routine.Result);
        Assert.DoesNotContain("원래 배치로 되돌렸습니다", routine.Result);
    }

    [Fact]
    public void LevelsThatDoNotMatchAfterApplyingAreReportedNotRolledBack()
    {
        // 배치 자체는 합법이라 되돌리지 않는다. 계산에 없는 효과가 걸려 있다는 것만 알린다.
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Levels[At(1, 0)] = 1;
        var command = Command(Charm(10, At(0, 0), At(1, 0)));
        command.ExpectedCellLevels[At(1, 0)] = 3;

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Equal(10, inventory.Cells[At(1, 0)]);
        Assert.StartsWith("자동 배치 완료 - 이동 1건, 회전 0건. 적용 뒤 레벨이 예상과 다른 칸 1개((1,0) 계산 3 != 게임 1)", routine.Result);
    }

    [Fact]
    public void AGuestWaitsForLevelsToCatchUpBeforeCallingThemWrong()
    {
        // 레벨은 서버가 다시 계산해 따로 동기화한다. 마지막 걸음 직후에는 옛 값이 남아 있으므로
        // 그만큼 기다린 뒤에 어긋났는지 본다.
        var (inventory, clock) = Guest();
        inventory.Cells[At(0, 0)] = 10;
        inventory.Levels[At(1, 0)] = 0;
        var command = Command(Charm(10, At(0, 0), At(1, 0)));
        command.ExpectedCellLevels[At(1, 0)] = 3;

        var routine = Routine(command, inventory, clock);
        var run = routine.Run();
        var frames = 0;
        while (run.MoveNext())
        {
            clock.Now += 0.1;
            inventory.Settle();
            if (++frames == 12) inventory.Levels[At(1, 0)] = 3;
        }

        Assert.Equal("자동 배치 완료 - 이동 1건, 회전 0건", routine.Result);
    }

    [Fact]
    public void TargetsAlreadyInPlaceAreNotTouched()
    {
        var (inventory, clock) = Host();
        inventory.Cells[At(0, 0)] = 10;
        var command = Command(Charm(10, At(0, 0), At(0, 0)));

        var routine = Routine(command, inventory, clock);
        Drive(routine, inventory, clock);

        Assert.Empty(inventory.Log);
        Assert.Equal("자동 배치 완료 - 이동 0건, 회전 0건", routine.Result);
    }
}
