using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class PadShortcutTests
{
    private static PadWindowAction Decide(
        bool pressed, bool gameUiOpen, bool settingsOpen = false,
        bool buildOpen = false, bool otherWindowOpen = false) =>
        PadShortcut.Decide(true, pressed, gameUiOpen, settingsOpen, buildOpen, otherWindowOpen);

    [Fact]
    public void OpensOnlyWhileAGameWindowIsUp()
    {
        Assert.Equal(PadWindowAction.Open, Decide(pressed: true, gameUiOpen: true));

        // 게임 창이 없으면 그 누름은 지도를 여는 누름이다.
        Assert.Equal(PadWindowAction.None, Decide(pressed: true, gameUiOpen: false));
        Assert.Equal(PadWindowAction.None, Decide(pressed: false, gameUiOpen: true));
    }

    [Fact]
    public void OpenedWindowsCloseWithTheSameButton()
    {
        Assert.Equal(
            PadWindowAction.CloseSettings,
            Decide(pressed: true, gameUiOpen: true, settingsOpen: true));
        Assert.Equal(
            PadWindowAction.CloseBuild,
            Decide(pressed: true, gameUiOpen: true, buildOpen: true));
    }

    [Fact]
    public void AnsweringWindowsAreNotCoveredUp()
    {
        Assert.Equal(
            PadWindowAction.None,
            Decide(pressed: true, gameUiOpen: true, otherWindowOpen: true));
    }

    [Fact]
    public void TurningItOffStopsEverything()
    {
        Assert.Equal(
            PadWindowAction.None,
            PadShortcut.Decide(
                enabled: false, pressed: true, gameUiOpen: true,
                settingsOpen: true, buildOpen: false, otherWindowOpen: false));
    }
}
