using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 우리 화면이 게임 UI 중 어디쯤에 그려지는지.
    ///
    /// 값은 게임에서 잰 것이다(인벤토리 덤프의 <c>[ui]</c> 절). 게임의 화면 공간 캔버스는
    /// <c>InteractableHUD</c>/<c>DynamicHUD</c> -2, <c>HUD</c> 0, <c>Panels</c> 2,
    /// <c>System</c> 10 이다.
    ///
    /// <b>Panels 위, System 아래에 둔다.</b> 세피라이트 보상 창과 레벨업 창이 <c>Panels</c>(2)에
    /// 있어서, 우리가 <c>HUD</c>(0) 그대로 있으면 무엇을 집을지 고르는 바로 그 순간에 덮여
    /// 보이지 않는다. 반대로 <c>System</c>(10)에는 게임 자신의 툴팁과 알림이 있으므로 그것까지
    /// 가리지는 않는다.
    /// </summary>
    internal static class Layers
    {
        public const int Hud = 5;
        public const int Window = 6;
        public const int Tooltip = 7;
    }

    /// <summary>
    /// 게임 조작을 가로막지 않도록 기본 그래픽의 레이캐스트를 끈 uGUI 팩터리.
    /// </summary>
    internal static class Widgets
    {
        /// <summary>
        /// 이 조각을 제 캔버스에 올려 그리는 순서를 정한다.
        ///
        /// 게임 HUD 밑에 그냥 달아 두면 부모 캔버스의 순서를 따르므로 <c>Panels</c> 에 가린다.
        /// 중첩 캔버스로 올리면 순서를 우리가 정할 수 있고, 부모의 <c>CanvasGroup</c> 은 그대로
        /// 상속되므로 게임이 UI 를 감출 때 함께 감춰지는 것은 유지된다.
        ///
        /// <paramref name="clickable"/> 은 누를 것이 있는 창에만 켠다. 레이캐스트는 캔버스 단위라
        /// 중첩 캔버스로 올리면 부모의 <c>GraphicRaycaster</c> 가 우리 안까지 훑지 않는다 -
        /// 켜지 않으면 창의 버튼이 눌리지 않는다.
        /// </summary>
        public static void Layer(GameObject go, int order, bool clickable)
        {
            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;

            if (clickable) go.AddComponent<GraphicRaycaster>();
        }

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
            // 비활성 창에서는 Awake 전에도 높이를 잰다. TMP의 UI 배율을 미리 맞춘다.
            text.isOrthographic = true;
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
        /// 글자를 받는 칸.
        ///
        /// <b>게임이 채팅에 쓰는 길 그대로다.</b> <c>UI_ChatInput</c>이 <c>TMP_InputField</c>를
        /// 두고 조작을 넘겨받는 순간 <c>ActivateInputField</c>를 부른다. 그 길이라 한글 IME 도
        /// 채팅과 똑같이 동작한다 - 우리가 조합 규칙을 흉내 내지 않는다. 다만 확인 입력만은
        /// 게임의 것을 쓸 수 없어 <see cref="PlannerInputField"/>로 막는다.
        ///
        /// <see cref="Clickable"/>과 같은 이유로 플레이어가 일부러 연 창에서만 쓴다. 여기서도
        /// <c>raycastTarget</c>이 켜지므로 HUD 에는 두지 않는다.
        /// </summary>
        public static TMP_InputField Input(
            string name, Transform parent, NativeSkin skin, float size, int characterLimit, float padding)
        {
            var rect = Rect(name, parent);
            var background = rect.gameObject.AddComponent<Image>();
            background.color = NativeSkin.EmptyFill;

            var viewport = Rect("Viewport", rect);
            Stretch(viewport, padding);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = Label("Text", viewport, skin, size, NativeSkin.TextBright, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            Stretch(text.rectTransform, 0f);

            var placeholder = Label("Placeholder", viewport, skin, size, NativeSkin.TextDim, TextAlignmentOptions.TopLeft);
            placeholder.textWrappingMode = TextWrappingModes.Normal;
            placeholder.overflowMode = TextOverflowModes.Overflow;
            Stretch(placeholder.rectTransform, 0f);

            var field = rect.gameObject.AddComponent<PlannerInputField>();
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = background;
            field.characterLimit = characterLimit;

            // 여러 줄로 접히되 Enter 는 보내기다. 게임 채팅과 같은 약속이라 따로 배울 것이 없다.
            field.lineType = TMP_InputField.LineType.MultiLineSubmit;
            field.customCaretColor = true;
            field.caretColor = NativeSkin.TextBright;
            field.selectionColor = new Color(NativeSkin.Mint.r, NativeSkin.Mint.g, NativeSkin.Mint.b, 0.35f);
            field.restoreOriginalTextOnEscape = false;
            field.text = "";
            return field;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
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

    /// <summary>
    /// <b>확인 입력을 받지 않는 글자 칸.</b> 적는 도중 스페이스를 누르면 그것이 전송으로 읽히던
    /// 것을 여기서 끊는다.
    ///
    /// 게임 어셈블리로 확인한 길은 이렇다. 게임의 UI 입력은 새 입력 시스템의
    /// <c>InputSystemUIInputModule</c>이고(<c>UIInputModule.currentModule</c>), 그 모듈은 확인
    /// 동작이 눌린 프레임에 지금 선택된 오브젝트로 <c>submitHandler</c>를 보낸다.
    /// <c>TMP_InputField</c>는 <c>ISubmitHandler</c>라 그것을 받아 <c>onSubmit</c>을 부르고
    /// <c>DeactivateInputField</c>까지 한다. 확인 동작에 스페이스가 함께 걸려 있으므로 띄어쓰기
    /// 한 번이 전송이었다.
    ///
    /// 모듈은 그 직전에 <c>updateSelectedHandler</c>를 보내고 칸이 키를 먹었으면 확인을 보내지
    /// 않는데, 새 입력 시스템의 동작은 그 프레임의 IMGUI 키 사건보다 먼저 읽히므로 그 방패가
    /// 눌린 바로 그 프레임에는 서지 않는다. 그래서 스페이스는 전송이 되면서 글자로 들어가지도
    /// 않았다.
    ///
    /// <b>Enter 는 그대로 보내기다.</b> 칸이 스스로 키를 읽는 길(<c>MultiLineSubmit</c>)이
    /// 남아 있어 거기서 <c>onSubmit</c>이 불린다. 한글 조합 중인 글자도 그 길이 마무리해 주므로,
    /// 우리가 Enter 를 직접 읽어 보내는 것보다 안전하다.
    ///
    /// 칸에 커서가 없을 때의 확인은 커서를 넣는 뜻으로 남긴다 - 컨트롤러로 칸을 골라 적기
    /// 시작하는 유일한 길이다.
    /// </summary>
    internal sealed class PlannerInputField : TMP_InputField
    {
        public override void OnSubmit(BaseEventData eventData)
        {
            if (!IsActive() || !IsInteractable() || isFocused) return;

            ActivateInputField();
            eventData?.Use();
        }
    }
}
