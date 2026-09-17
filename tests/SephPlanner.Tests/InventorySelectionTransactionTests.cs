using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class InventorySelectionTransactionTests
{
    [Fact]
    public void SelectedDestinationIsOnlyReadAfterBothInventoryAndEffectCoordinatesMove()
    {
        var destination = new object();
        object? selected = destination;
        var inventoryMoved = false;
        var effectMoved = false;
        var reads = 0;
        void ReadSelection()
        {
            if (selected == null) return;
            Assert.Equal(inventoryMoved, effectMoved);
            reads++;
        }

        InventorySelectionTransaction.Run(destination, () => selected, value =>
        {
            selected = value;
            ReadSelection();
        }, () => true, () =>
        {
            inventoryMoved = true;
            ReadSelection();
            effectMoved = true;
        }, ex => throw new InvalidOperationException("표시 복원 실패", ex));

        Assert.Same(destination, selected);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void CorruptStateDoesNotReopenTheSelectedItem()
    {
        var destination = new object();
        object? selected = destination;
        var failure = new InvalidOperationException("교환 중단");

        var actual = Assert.Throws<InvalidOperationException>(() =>
            InventorySelectionTransaction.Run(destination, () => selected, value => selected = value,
                () => false, () => throw failure, _ => Assert.Fail("표시 복원을 호출하면 안 됩니다.")));

        Assert.Same(failure, actual);
        Assert.Null(selected);
    }

    [Fact]
    public void DisplayFailureDoesNotReplaceTheOriginalWriteException()
    {
        var destination = new object();
        object? selected = destination;
        var writeFailure = new InvalidOperationException("교환 오류");
        var displayFailure = new InvalidOperationException("표시 오류");
        Exception? reported = null;

        var actual = Assert.Throws<InvalidOperationException>(() =>
            InventorySelectionTransaction.Run(destination, () => selected, value =>
            {
                if (value != null) throw displayFailure;
                selected = null;
            }, () => true, () => throw writeFailure, ex => reported = ex));

        Assert.Same(writeFailure, actual);
        Assert.Same(displayFailure, reported);
    }

    [Fact]
    public void ANewSelectionIsNotOverwritten()
    {
        var original = new object();
        var other = new object();
        object? selected = original;

        InventorySelectionTransaction.Run(original, () => selected, value => selected = value,
            () => true, () => selected = other, _ => Assert.Fail("복원 오류"));

        Assert.Same(other, selected);
    }
}
