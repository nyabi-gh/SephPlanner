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

    /// <summary>
    /// 패드로 창을 여닫을 때인지 가린다.
    ///
    /// <b>게임은 패드 버튼을 하나도 남기지 않고 쓴다</b>(docs/RESEARCH.md 의 "패드 단축키").
    /// 키보드의 F 키 같은 빈자리가 없어서, 아무 버튼이나 잡으면 누를 때마다 게임 동작이 함께
    /// 난다 - 우리는 입력을 가져가지 않으므로 막을 길이 없다.
    ///
    /// 빈틈이 하나 있다. 지도를 여는 View(<c>select</c>)는 <c>HandleOnOpenMapPanel</c>이
    /// <b>컨트롤 스택이 비었을 때만</b> 지도를 열고 UI 액션 맵에는 아예 없다. 즉 게임 창이
    /// 떠 있는 동안 이 버튼은 게임에서 아무 일도 하지 않는다. 그 조건을 그대로 우리 조건으로
    /// 삼으면 한 누름에 둘이 함께 나지 않는다.
    /// </summary>
    internal static class PadShortcut
    {
        public static PadWindowAction Decide(
            bool enabled, bool pressed, bool gameUiOpen,
            bool settingsOpen, bool buildOpen, bool otherWindowOpen)
        {
            if (!enabled || !pressed) return PadWindowAction.None;
            // 우리 창이 떠 있으면 그것을 닫는 뜻이다. 패드에는 ESC 자리가 start 하나뿐이라
            // 연 버튼으로 닫히지 않으면 닫는 길을 따로 외워야 한다.
            if (settingsOpen) return PadWindowAction.CloseSettings;
            if (buildOpen) return PadWindowAction.CloseBuild;

            // 진단 동의·업데이트 창은 물음에 답해야 닫힌다. 그 위에 우리 창을 얹지 않는다.
            if (otherWindowOpen) return PadWindowAction.None;

            // 게임 창이 없으면 이 누름은 지도를 여는 누름이다. 우리가 끼어들면 지도와 우리 창이
            // 함께 뜬다.
            return gameUiOpen ? PadWindowAction.Open : PadWindowAction.None;
        }
    }
}
