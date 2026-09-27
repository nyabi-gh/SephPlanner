using System;
using System.Reflection;
using HarmonyLib;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임은 석판 회전 알림을 받으면 가방이 닫혀 있어도 석판 툴팁을 연다
    /// (<c>HandleTabletRotated</c> → <c>OnItemSelected</c> → <c>UI_StoneTabletTooltip.Open</c>).
    /// 게임 자신은 가방 창 안에서만 돌리므로 드러나지 않지만, 참가자의 자동 배치는 서버의
    /// <c>StoneTablet.Rotate</c> 가 이 알림을 보내 닫힌 가방 위로 툴팁이 떠서 남는다.
    ///
    /// 선택된 칸만 보면 들어 올린 석판의 회전(<c>UI_NewItemPicker.CurrentPickedUp</c>)까지 막으므로
    /// 가방 창이 열려 있는지로 가른다.
    /// </summary>
    internal static class TabletRotationTooltipGuard
    {
        public static void Install(Harmony harmony, Action<object> warn)
        {
            var target = typeof(UI_CharacterStatusPanel).GetMethod(
                "HandleTabletRotated", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(StoneTablet), typeof(int) }, null);
            if (target == null)
            {
                warn("게임의 UI_CharacterStatusPanel.HandleTabletRotated 를 찾지 못했습니다. 참가자 자동 배치 뒤 석판 툴팁이 남을 수 있습니다.");
                return;
            }

            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(TabletRotationTooltipGuard), nameof(Prefix)));
            }
            catch (Exception ex)
            {
                warn("석판 회전 알림 가드를 걸지 못했습니다: " + ex);
            }
        }

        private static bool Prefix(UI_CharacterStatusPanel __instance) => __instance.IsOpened;
    }
}
