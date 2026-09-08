using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class ShortcutCaptureTests
{
    private enum Key { Ctrl, Shift, Enter, F8, Escape, A }

    private static ShortcutCapture<Key> Create() =>
        new(key => key is Key.Ctrl or Key.Shift, Key.Escape);

    [Fact]
    public void ActivationKeyMustBeReleasedBeforeCapturing()
    {
        var capture = Create();
        capture.Begin(10);
        Assert.True(capture.Update(10, [Key.Enter], [Key.Enter], out var key));
        Assert.Null(key);
        Assert.True(capture.Update(11, [Key.Enter], [], out key));
        Assert.Null(key);
        Assert.True(capture.Update(12, [], [], out key));
        Assert.Null(key);
        Assert.True(capture.Update(13, [Key.F8], [Key.F8], out key));
        Assert.Equal(Key.F8, key);
    }

    [Fact]
    public void ModifiersWaitForTheMainKeyAndReleaseBlocksExistingShortcuts()
    {
        var capture = Create();
        capture.Begin(1);
        capture.Update(2, [], [], out _);
        Assert.True(capture.Update(3, [Key.Ctrl, Key.Shift], [Key.Ctrl, Key.Shift], out var key));
        Assert.Null(key);
        Assert.True(capture.Capturing);
        Assert.True(capture.Update(4, [Key.Ctrl, Key.Shift, Key.A], [Key.A], out key));
        Assert.Equal(Key.A, key);
        Assert.False(capture.Capturing);
        Assert.True(capture.Update(5, [Key.Ctrl], [], out key));
        Assert.Null(key);
        Assert.True(capture.Update(6, [], [], out _));
        Assert.False(capture.Update(7, [Key.F8], [Key.F8], out _));
    }

    [Fact]
    public void EscapeWinsOverOtherPressedKeysAndDoesNotBind()
    {
        var capture = Create();
        capture.Begin(1);
        capture.Update(2, [], [], out _);
        Assert.True(capture.Update(3, [Key.A, Key.Escape], [Key.A, Key.Escape], out var key));
        Assert.Null(key);
        Assert.False(capture.Capturing);
        Assert.True(capture.BlocksShortcuts);
        Assert.True(capture.Update(4, [Key.Escape], [], out _));
        Assert.True(capture.Update(5, [], [], out _));
        Assert.False(capture.BlocksShortcuts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingOrLosingFocusCancelsWithoutLeakingThePendingKey(bool listening)
    {
        var capture = Create();
        capture.Begin(1);
        if (listening) capture.Update(2, [], [], out _);
        capture.Cancel();
        Assert.True(capture.Update(3, [Key.F8], [Key.F8], out var key));
        Assert.Null(key);
        capture.Update(4, [], [], out _);
        Assert.False(capture.BlocksShortcuts);
    }

    [Fact]
    public void ANewCaptureDoesNotReuseThePreviousKey()
    {
        var capture = Create();
        capture.Begin(1);
        capture.Update(2, [], [], out _);
        capture.Update(3, [Key.A], [Key.A], out _);
        capture.Begin(4);
        capture.Update(5, [Key.A], [], out var key);
        Assert.Null(key);
        capture.Update(6, [], [], out _);
        capture.Update(7, [Key.Enter], [Key.Enter], out key);
        Assert.Equal(Key.Enter, key);
    }
}
