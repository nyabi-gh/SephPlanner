using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 패드를 읽는 자리.
    ///
    /// <b>구식 <c>Input</c> 의 조이스틱 KeyCode 로는 읽지 않는다.</b> BepInEx 단축키가 타는
    /// <c>UnityInput</c> 은 구식과 새 입력 시스템 두 구현 중 하나로 잡히는데, 새 쪽에는 조이스틱
    /// 버튼이 아예 없고 구식 쪽도 버튼 번호가 기기마다 다르다. 게임이 새 InputSystem 을 쓰므로
    /// 마우스와 같은 길로 읽는다(<c>SephPlannerPlugin.Cursor</c>).
    /// </summary>
    internal sealed class PadInput
    {
        /// <summary><c>PlayerInputController.GamepadScheme</c> 과 같은 값.</summary>
        private const string GamepadScheme = "Gamepad";

        private readonly PadShortcut _shortcut = new PadShortcut();

        public void Capture()
        {
            var updateType = InputState.currentUpdateType;
            if (updateType != InputUpdateType.Dynamic && updateType != InputUpdateType.Fixed &&
                updateType != InputUpdateType.Manual) return;
            _shortcut.Capture(InputState.updateCount, GameWindow(), MapOpen());
        }

        public void Clear() => _shortcut.Clear();

        public PadWindowAction Decide(bool enabled, bool settingsOpen, bool buildOpen, bool otherWindowOpen)
        {
            var pad = Gamepad.current;
            return _shortcut.Decide(
                InputState.updateCount, enabled, pad != null && pad.selectButton.wasPressedThisFrame,
                GameWindow(), MapOpen(), settingsOpen, buildOpen, otherWindowOpen);
        }

        /// <summary>
        /// 지금 패드로 놀고 있는가. 꽂혀 있는지가 아니라 게임이 어느 조작 방식으로 보고 있는지를
        /// 본다 - 꽂아 둔 채 키보드로 하는 사람에게 패드 안내를 띄우면 안 된다.
        ///
        /// <c>ControlsChangeHandler.IsUsingKeyboardAndMouse</c> 는 쓰지 않는다. 그 값은 조작이 한 번
        /// 바뀌어야 채워져서, 켜자마자는 기본값 <c>false</c>(=패드)로 보인다.
        /// </summary>
        public static bool InUse()
        {
            // 패드가 없는 사람에게는 여기서 끝난다. 아래 두 줄은 패드를 꽂은 사람만 치른다.
            if (Gamepad.current == null) return false;

            var handler = ControlsChangeHandler.Current;
            var input = handler != null ? handler.PlayerInput : null;
            return input != null && input.currentControlScheme == GamepadScheme;
        }

        private static object GameWindow() =>
            UIManager.Instance != null ? UIManager.Instance.CurrentControlStack : null;

        private static bool MapOpen()
        {
            var map = UIManager.Instance != null ? UIManager.Instance.GetElement<UI_MapPanel>() : null;
            return map != null && map.IsOpened;
        }
    }
}
