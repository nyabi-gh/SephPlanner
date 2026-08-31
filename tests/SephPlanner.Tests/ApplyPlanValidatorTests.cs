using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;

namespace SephPlanner.Tests;

public class ApplyPlanValidatorTests
{
    [Fact]
    public void AnUnchangedInventoryPasses()
    {
        Assert.Null(Validate(Command(), LiveItems()));
    }

    [Fact]
    public void AChangedPositionStopsTheWholePlan()
    {
        var items = LiveItems();
        items[1].Position = new GridPos(3, 0);

        Assert.Contains("인벤토리가 바뀌어", Validate(Command(), items));
    }

    [Fact]
    public void ANewItemStopsTheWholePlan()
    {
        var items = LiveItems();
        items.Add(new LivePlanItem { InstanceId = 30, Position = new GridPos(3, 0) });

        Assert.Contains("인벤토리가 바뀌어", Validate(Command(), items));
    }

    [Fact]
    public void AChangedTabletRotationStopsTheWholePlan()
    {
        var items = LiveItems();
        items[0].Rotation = 2;

        Assert.Contains("인벤토리가 바뀌어", Validate(Command(), items));
    }

    [Fact]
    public void ANewRotationLockStopsTheWholePlan()
    {
        var items = LiveItems();
        items[0].CanRotate = false;

        Assert.Contains("회전할 수 없는", Validate(Command(), items));
    }

    [Fact]
    public void DuplicateDestinationsAreRejected()
    {
        var command = Command();
        command.Targets[1].To = command.Targets[0].To;

        Assert.Contains("목표 칸", Validate(command, LiveItems()));
    }

    [Fact]
    public void AChangedGridStopsTheWholePlan()
    {
        Assert.Contains(
            "인벤토리가 바뀌어",
            ApplyPlanValidator.Validate(Command(), LiveItems(), width: 6, height: 7, storage: 12));
    }

    private static ApplyPlanCommand Command() => new()
    {
        ExpectedWidth = 6,
        ExpectedHeight = 7,
        ExpectedStorage = 6,
        Targets =
        {
            new PlanTarget
            {
                InstanceId = 1,
                From = new GridPos(0, 0),
                To = new GridPos(1, 0),
                IsTablet = true,
                FromRotation = 0,
                Rotation = 1,
            },
            new PlanTarget
            {
                InstanceId = 20,
                From = new GridPos(1, 0),
                To = new GridPos(0, 0),
            },
        },
    };

    private static List<LivePlanItem> LiveItems() =>
    [
        new LivePlanItem
        {
            InstanceId = 1,
            Position = new GridPos(0, 0),
            IsTablet = true,
            Rotation = 0,
            CanRotate = true,
        },
        new LivePlanItem { InstanceId = 20, Position = new GridPos(1, 0) },
    ];

    private static string? Validate(ApplyPlanCommand command, IReadOnlyCollection<LivePlanItem> items) =>
        ApplyPlanValidator.Validate(command, items, width: 6, height: 7, storage: 6);
}
