#nullable enable

namespace SephPlanner.Plugin
{
    /// <summary>패드의 View 버튼을 눌렀을 때 창에 할 일.</summary>
    internal enum PadWindowAction
    {
        None,
        Open,
        CloseSettings,
        CloseBuild,
    }

    /// <summary>게임의 입력 처리 전후에 같은 창이 유지되는 View 입력만 받는다.</summary>
    internal sealed class PadShortcut
    {
        private uint? _update;
        private object? _windowBefore;

        public void Capture(uint update, object? gameWindow, bool mapOpen)
        {
            if (_update == update) return;
            _update = update;
            // 지도는 다른 창 아래에 있어도 View로 닫히므로 함께 처리하면 안 된다.
            _windowBefore = mapOpen ? null : gameWindow;
        }

        public void Clear()
        {
            _update = null;
            _windowBefore = null;
        }

        public PadWindowAction Decide(
            uint update, bool enabled, bool pressed, object? gameWindow, bool mapOpen,
            bool settingsOpen, bool buildOpen, bool otherWindowOpen)
        {
            if (_update != update || !pressed) return PadWindowAction.None;
            var sameWindow = _windowBefore != null && ReferenceEquals(_windowBefore, gameWindow);
            _windowBefore = null;
            if (!enabled || !sameWindow || mapOpen) return PadWindowAction.None;
            if (otherWindowOpen) return PadWindowAction.None;

            if (settingsOpen) return PadWindowAction.CloseSettings;
            if (buildOpen) return PadWindowAction.CloseBuild;
            return PadWindowAction.Open;
        }
    }
}
