using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class PadShortcutTests
{
    private readonly PadShortcut _shortcut = new();
    private readonly object _window = new();

    private PadWindowAction Decide(
        bool pressed = true, bool enabled = true, bool mapOpen = false,
        bool settingsOpen = false, bool buildOpen = false, bool otherWindowOpen = false,
        uint update = 1) =>
        _shortcut.Decide(update, enabled, pressed, _window, mapOpen, settingsOpen, buildOpen, otherWindowOpen);

    [Fact]
    public void OpensOnlyWhileTheSameGameWindowIsUp()
    {
        _shortcut.Capture(1, _window, mapOpen: false);

        Assert.Equal(PadWindowAction.None, Decide(pressed: false));
        Assert.Equal(PadWindowAction.Open, Decide());
    }

    [Fact]
    public void AMapOpenedByThisPressDoesNotAlsoOpenOurWindow()
    {
        _shortcut.Capture(1, null, mapOpen: false);

        Assert.Equal(PadWindowAction.None, Decide(mapOpen: true));
    }

    [Fact]
    public void ClosingAMapAboveAnotherWindowDoesNotOpenOurWindow()
    {
        _shortcut.Capture(1, new object(), mapOpen: true);

        Assert.Equal(PadWindowAction.None, Decide());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMapUnderAnotherWindowStillOwnsThePress(bool settingsOpen)
    {
        _shortcut.Capture(1, _window, mapOpen: true);

        Assert.Equal(PadWindowAction.None, Decide(settingsOpen: settingsOpen));
    }

    [Fact]
    public void AMapOpenedDuringInputBlocksOurWindow()
    {
        _shortcut.Capture(1, _window, mapOpen: false);

        Assert.Equal(PadWindowAction.None, Decide(mapOpen: true));
    }

    [Fact]
    public void ReplacingTheGameWindowDoesNotReuseItsPress()
    {
        _shortcut.Capture(1, new object(), mapOpen: false);

        Assert.Equal(PadWindowAction.None, Decide());
    }

    [Fact]
    public void AWindowOpenedDuringInputDoesNotCountAsAlreadyOpen()
    {
        _shortcut.Capture(1, null, mapOpen: false);

        Assert.Equal(PadWindowAction.None, Decide());
    }

    [Fact]
    public void ClosingTheGameWindowDoesNotOpenOurWindow()
    {
        _shortcut.Capture(1, _window, mapOpen: false);

        Assert.Equal(PadWindowAction.None,
            _shortcut.Decide(1, true, true, null, false, false, false, false));
    }

    [Fact]
    public void OpenedWindowsCloseWithTheSameButton()
    {
        _shortcut.Capture(1, _window, mapOpen: false);
        Assert.Equal(PadWindowAction.CloseSettings, Decide(settingsOpen: true));

        _shortcut.Capture(2, _window, mapOpen: false);
        Assert.Equal(PadWindowAction.CloseBuild, Decide(buildOpen: true, update: 2));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnsweringWindowsKeepTheirBackgroundWindows(bool settingsOpen, bool buildOpen)
    {
        _shortcut.Capture(1, _window, mapOpen: false);

        Assert.Equal(PadWindowAction.None,
            Decide(settingsOpen: settingsOpen, buildOpen: buildOpen, otherWindowOpen: true));
    }

    [Fact]
    public void TurningItOffStopsEverything()
    {
        _shortcut.Capture(1, _window, mapOpen: false);

        Assert.Equal(PadWindowAction.None, Decide(enabled: false, settingsOpen: true));
        Assert.Equal(PadWindowAction.None, Decide());
    }

    [Fact]
    public void TheSameInputUpdateCannotToggleTwice()
    {
        _shortcut.Capture(1, _window, mapOpen: false);
        Assert.Equal(PadWindowAction.Open, Decide());

        _shortcut.Capture(1, _window, mapOpen: false);
        Assert.Equal(PadWindowAction.None, Decide(settingsOpen: true));
    }

    [Fact]
    public void OnlyTheCapturedInputUpdateCanOpenAWindow()
    {
        Assert.Equal(PadWindowAction.None, Decide());

        _shortcut.Capture(1, _window, mapOpen: false);
        Assert.Equal(PadWindowAction.None, Decide(update: 2));

        _shortcut.Capture(2, _window, mapOpen: false);
        Assert.Equal(PadWindowAction.Open, Decide(update: 2));
    }

    [Fact]
    public void DisablingInputClearsTheCapturedContext()
    {
        _shortcut.Capture(1, _window, mapOpen: false);
        _shortcut.Clear();

        Assert.Equal(PadWindowAction.None, Decide());
    }
}
