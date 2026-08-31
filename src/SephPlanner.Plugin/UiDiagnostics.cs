using System;
using System.Text;
using SephPlanner.Plugin.Ui;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 우리 화면이 왜 안 보이는지 판단할 재료.
    ///
    /// 세피라이트 보상 창이나 레벨업 창이 열리면 우리 화면이 통째로 사라진다. 우리가 게임 HUD
    /// 캔버스(<c>UIRoot</c>) 밑에 달려 있어서 그 <c>CanvasGroup</c> 알파가 0 이 되면 딸려가기
    /// 때문일 것으로 보이는데, <b>무엇이 그 알파를 0 으로 만드는지는 아직 확인되지 않았다.</b>
    /// 화면이 그냥 가려진 것일 수도 있다(보상 창이 더 위에 그려지고 불투명하면 그렇다).
    ///
    /// 둘은 고치는 방법이 다르다. 그래서 짐작으로 고치지 않고, 창이 열린 순간의 상태를 그대로
    /// 적어 두고 그걸 보고 판단한다. 인벤토리 덤프 단축키에 함께 실려 나간다.
    /// </summary>
    internal static class UiDiagnostics
    {
        public static void Write(StringBuilder text)
        {
            text.AppendLine();
            text.AppendLine("[ui]");

            var ui = UIManager.Instance;
            if (ui == null)
            {
                text.AppendLine("  UIManager 없음");
                return;
            }

            WriteRoots(text, ui);
            WriteOurs(text);
            WriteOpenPanels(text);
        }

        /// <summary>
        /// 풀링 부모마다 UIRoot 가 하나씩 있다. 우리는 HUD 밑에 달려 있으므로 그 알파가 0 이면
        /// 우리도 0 이다. 다른 루트의 알파와 견주면 게임이 전체를 감춘 것인지 HUD 만 감춘 것인지
        /// 갈린다.
        /// </summary>
        private static void WriteRoots(StringBuilder text, UIManager ui)
        {
            foreach (EUIObjectPoolingParent kind in Enum.GetValues(typeof(EUIObjectPoolingParent)))
            {
                UIRoot root = null;
                try
                {
                    root = ui.GetRootFromType(kind);
                }
                catch (Exception)
                {
                    // 쓰지 않는 부모는 루트가 없을 수 있다. 없다는 것도 정보다.
                }

                if (root == null)
                {
                    text.AppendLine($"  root {kind} = 없음");
                    continue;
                }

                var group = root.GetComponent<CanvasGroup>();
                var canvas = root.Canvas;
                text.AppendLine(
                    $"  root {kind} active={root.gameObject.activeInHierarchy} " +
                    $"alpha={(group == null ? "없음" : group.alpha.ToString("0.##"))} " +
                    $"canvas={(canvas == null ? "없음" : canvas.sortingOrder + "/" + canvas.renderMode)}");
            }
        }

        private static void WriteOurs(StringBuilder text)
        {
            foreach (var widget in UnityEngine.Object.FindObjectsByType<SephPlannerWidget>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (widget == null) continue;

                var go = widget.gameObject;
                var group = go.GetComponent<CanvasGroup>();
                var rect = go.transform as RectTransform;
                var corners = new Vector3[4];
                if (rect != null) rect.GetWorldCorners(corners);

                text.AppendLine(
                    $"  ours {go.name} active={go.activeInHierarchy} " +
                    $"alpha={(group == null ? "없음" : group.alpha.ToString("0.##"))} " +
                    $"parent={(go.transform.parent == null ? "없음" : go.transform.parent.name)} " +
                    $"corners={corners[0]}~{corners[2]}");
            }
        }

        /// <summary>
        /// 지금 열려 있는 게임 창들. <c>UIBase</c>의 public 필드를 그대로 찍는 것은, HUD 를 감추라는
        /// 뜻의 값이 그중에 있는지 이름을 몰라도 알아보기 위해서다. 있으면 그 창만 걸러 낼 수 있고,
        /// 없으면 우리 캔버스를 따로 두는 수밖에 없다.
        /// </summary>
        private static void WriteOpenPanels(StringBuilder text)
        {
            var fields = typeof(UIBase).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var panel in UnityEngine.Object.FindObjectsByType<UIBase>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (panel == null || !panel.IsOpened) continue;

                var canvas = panel.GetComponentInParent<Canvas>();
                var values = new StringBuilder();
                foreach (var field in fields)
                {
                    object value;
                    try
                    {
                        value = field.GetValue(panel);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    // 참조형은 무엇이 켜져 있는지 알려주지 않는다. 껐다 켜는 값만 본다.
                    if (value is bool || value is int || (value != null && value.GetType().IsEnum))
                        values.Append(' ').Append(field.Name).Append('=').Append(value);
                }

                text.AppendLine(
                    $"  open {panel.GetType().Name} " +
                    $"canvas={(canvas == null ? "없음" : canvas.name + " " + canvas.sortingOrder)}" +
                    values);
            }
        }
    }
}
