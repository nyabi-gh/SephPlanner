using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// uGUI 조각을 만드는 손. XAML 이 없으니 만드는 일이 코드로 오는데, 같은 다섯 줄을 곳곳에
    /// 되풀이하지 않으려고 모아 두었다.
    ///
    /// <b>그리는 것마다 <c>raycastTarget</c>이 꺼져 있다.</b> 마우스를 그대로 통과시켜야 게임
    /// 조작을 방해하지 않는다. 여기를 거치지 않고 직접 Image/Text 를 붙이면 그 보장이 깨진다.
    /// </summary>
    internal static class Widgets
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static Image Fill(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.color = color;
            return image;
        }

        public static TextMeshProUGUI Label(
            string name, Transform parent, NativeSkin skin, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;

            if (skin.Font != null)
            {
                text.font = skin.Font;

                // 게임이 쓰는 재질을 그대로 쓰면 외곽선·그림자까지 같아진다. 픽셀 글꼴이라
                // 재질이 다르면 흐릿해져 한눈에 티가 난다.
                if (skin.FontMaterial != null) text.fontSharedMaterial = skin.FontMaterial;
            }
            return text;
        }

        /// <summary>
        /// 누를 수 있는 글자. <b>여기만 <c>raycastTarget</c>이 켜져 있다.</b> HUD 화면은 입력을
        /// 하나도 가져가지 않는 것이 게임을 방해하지 않는다는 보장의 근거이므로, 이 손은 설정
        /// 창처럼 플레이어가 일부러 연 화면에서만 쓴다(<see cref="SettingsWindow"/>).
        ///
        /// 글자 자체를 버튼의 그래픽으로 삼는다. 마우스를 올리면 색이 밝아지는 것이 그래서 공짜다.
        /// </summary>
        public static TextMeshProUGUI Clickable(
            string name, Transform parent, NativeSkin skin, float size, Color color,
            UnityEngine.Events.UnityAction onClick)
        {
            var text = Label(name, parent, skin, size, color);
            text.raycastTarget = true;

            var button = text.gameObject.AddComponent<Button>();
            button.targetGraphic = text;

            var colors = button.colors;
            colors.normalColor = new Color(0.72f, 0.72f, 0.72f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.55f, 0.55f, 0.55f);
            button.colors = colors;
            button.onClick.AddListener(onClick);
            return text;
        }

        /// <summary>
        /// 누를 수 있는 한 줄. 이름과 값이 따로 있는 목록에서는 글자 하나가 아니라 줄 전체가
        /// 눌려야 하므로, 줄의 바탕칠 자체를 버튼의 그래픽으로 삼는다. 안에 넣는 글자는
        /// <see cref="Label"/>로 만들어 <c>raycastTarget</c>이 꺼져 있으니 클릭이 뒤로 통과한다.
        ///
        /// <see cref="Clickable"/>과 같은 이유로 플레이어가 일부러 연 창에서만 쓴다.
        /// </summary>
        public static Image ClickableRow(
            string name, Transform parent, Color fill, UnityEngine.Events.UnityAction onClick)
        {
            var image = Fill(name, parent, fill);
            image.raycastTarget = true;

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            // 바탕칠에 곱해지는 값이다. 평소를 어둡게 두어야 마우스를 올렸을 때 밝아지는 것이 보인다.
            var colors = button.colors;
            colors.normalColor = new Color(0.6f, 0.6f, 0.6f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(0.6f, 0.6f, 0.6f);
            colors.pressedColor = new Color(0.45f, 0.45f, 0.45f);
            button.colors = colors;
            button.onClick.AddListener(onClick);
            return image;
        }

        /// <summary>
        /// 여러 줄로 흐르는 글. <see cref="Label"/>은 한 줄로 두고 넘치면 잘라내는데, 툴팁처럼
        /// 문장이 오는 자리는 접혀야 한다. 높이는 <see cref="FitHeight"/>로 재어 걸어 준다.
        /// </summary>
        public static TextMeshProUGUI Paragraph(
            string name, Transform parent, NativeSkin skin, float size, Color color)
        {
            var text = Label(name, parent, skin, size, color, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        /// <summary>
        /// 접히는 글의 높이를 정한다.
        ///
        /// 세로 배치 안에서 TMP 에게 높이를 물어보게 두면(<c>ContentSizeFitter</c>와 겹칠 때 특히)
        /// 폭이 정해지기 전에 재는 순번이 생겨 한 줄로 눌리거나 0 이 된다. 그래서 폭을 우리가
        /// 넘겨 주고 높이를 직접 받아 고정한다 - 순번에 기대지 않으니 결과가 늘 같다.
        /// </summary>
        public static void FitHeight(TextMeshProUGUI text, LayoutElement element, float width)
        {
            if (text == null || element == null) return;

            var height = text.text.Length == 0
                ? 0f
                : text.GetPreferredValues(text.text, width, 0f).y;

            element.minHeight = height;
            element.preferredHeight = height;
        }

        public static VerticalLayoutGroup Column(
            RectTransform rect, float spacing, RectOffset padding = null)
        {
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset(0, 0, 0, 0);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup Row(RectTransform rect, float spacing)
        {
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return group;
        }

        public static ContentSizeFitter Fitter(RectTransform rect)
        {
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return fitter;
        }

        public static LayoutElement Fixed(RectTransform rect, float height, float width = -1)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            if (width >= 0)
            {
                element.preferredWidth = width;
                element.minWidth = width;
            }
            else
            {
                element.flexibleWidth = 1;
            }
            return element;
        }

        public static void SetActive(Component component, bool active)
        {
            if (component != null && component.gameObject.activeSelf != active)
                component.gameObject.SetActive(active);
        }
    }
}
