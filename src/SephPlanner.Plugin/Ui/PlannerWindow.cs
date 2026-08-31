using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 우리가 직접 그리는 창의 뼈대. 설정 창과 빌드 창이 이것을 나눠 쓴다.
    ///
    /// <b>게임 설정 창에 탭으로 붙이는 길은 접었다.</b> 그 창은 탭 다섯 개에 딱 맞게 짜여 있어
    /// (줄 폭 382 = 74x5 + 3x4) 여섯째가 들어갈 자리가 없고, 선택 표시인 흰 돌기가 탭 그림마다
    /// 박혀 있어 버튼을 옮기면 게임 탭들이 저마다 어긋난다. 우리 창은 폭도 줄 수도 우리가
    /// 정하므로 그 한계가 없다(docs/RESEARCH.md).
    ///
    /// <b>이 창들은 입력을 받는다.</b> HUD 화면(<see cref="NativeHud"/>)이 입력을 하나도 가져가지
    /// 않는 것은 플레이 중 게임 조작을 방해하지 않기 위해서인데, 창은 플레이어가 일부러 연
    /// 것이라 그 이유가 걸리지 않는다. 대신 <see cref="PlannerPanel"/>이 게임의 <c>UIBase</c>를
    /// 상속해 컨트롤 스택에 올라간다 - 그러면 게임이 여는 동안 캐릭터 조작을 막고 ESC 로 닫아
    /// 주므로, 우리가 흉내 낼 것이 없고 게임의 다른 창과 똑같이 행동한다. 닫으면 그 자리에
    /// 아무것도 남지 않아 HUD 의 무입력 보장은 그대로다.
    /// </summary>
    internal abstract class PlannerWindow
    {
        private PlannerPanel _panel;
        private TextMeshProUGUI _hint;

        protected NativeSkin Skin { get; private set; }

        /// <summary>기준 크기. 창의 모든 치수가 여기에 대한 비율이다.</summary>
        protected float Base { get; private set; }

        public bool IsOpen => _panel != null && _panel.IsOpened;
        public string Blocker { get; private set; } = "";
        public string Origin { get; protected set; } = "";

        protected abstract string Title { get; }

        /// <summary>창의 가로 폭(기준 크기의 배수).</summary>
        protected abstract float WidthRatio { get; }

        protected abstract void BuildBody(RectTransform content);

        /// <summary>열기 직전과 값이 바뀐 뒤에 지금 상태로 내용을 맞춘다.</summary>
        public virtual void Refresh()
        {
        }

        /// <summary>컨트롤러로 열었을 때 초점을 줄 곳. 없으면 화살표를 누를 방법이 없다.</summary>
        protected virtual GameObject DefaultFocus => null;

        /// <summary>열려 있으면 닫고, 아니면 연다. 창이 아직 없으면 이때 만든다.</summary>
        public void Toggle(string hint)
        {
            if (!TryCreate()) return;

            if (_panel.IsOpened)
            {
                _panel.Close();
                return;
            }

            _hint.text = hint;
            Refresh();
            _panel.defaultSelectable = DefaultFocus;
            _panel.Open();
        }

        public void Close()
        {
            if (IsOpen) _panel.Close();
        }

        public void Destroy()
        {
            if (_panel != null) Object.Destroy(_panel.gameObject);
            _panel = null;
            Cleared();
        }

        /// <summary>창이 사라졌으니 들고 있던 조각도 버려야 한다.</summary>
        protected virtual void Cleared()
        {
        }

        protected float S(float ratio) => Base * ratio;

        private bool TryCreate()
        {
            if (_panel != null) return true;
            if (UIManager.Instance == null)
            {
                Blocker = "UIManager 가 아직 없습니다.";
                return false;
            }

            var root = UIManager.Instance.GetRootFromType(EUIObjectPoolingParent.HUD);
            if (root == null || root.Canvas == null)
            {
                Blocker = "HUD 캔버스(UIRoot)를 찾지 못했습니다.";
                return false;
            }

            Blocker = "";
            Skin = NativeSkin.Borrow(root);
            Base = Skin.BaseSize;

            // 창이 밖에서 파괴됐으면 Destroy() 를 거치지 않았다. 죽은 줄 위에 덧짓지 않는다.
            Cleared();
            Build(root);
            Origin = Skin.Origin;
            return true;
        }

        private void Build(UIRoot root)
        {
            var frame = Widgets.Fill("SephPlanner" + GetType().Name, root.transform, NativeSkin.Frame);
            var go = frame.gameObject;

            // 컴포넌트를 붙이기 전에 꺼 둔다. UIBase 는 열릴 때 Awake 가 돌면서 CanvasGroup 을
            // 잡아 두는데, 켜진 채로 붙이면 그 전에 Awake 가 돌아 버린다.
            go.SetActive(false);
            go.AddComponent<CanvasGroup>();
            go.AddComponent<SephPlannerWidget>();

            // 게임 창 위에 뜬다. 보상 창을 열어 둔 채로 빌드 창을 열 수 있어야 하기 때문이다.
            // 여기는 누를 것이 있으므로 레이캐스터도 함께 올린다.
            Widgets.Layer(go, Layers.Window, clickable: true);

            _panel = go.AddComponent<PlannerPanel>();
            _panel.hasControl = true;
            _panel.canCloseControlWithESC = true;

            // 게임이 다른 플레이어에게 "메뉴 보는 중"으로 알리는 값이다. 우리 창도 같은 성격이다.
            _panel.isPlayerUITHing = true;
            _panel.SetRoot(root);

            var rect = frame.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(S(WidthRatio), 0f);

            var edge = Mathf.Max(1, Mathf.RoundToInt(S(0.25f)));
            Widgets.Column(rect, 0f, new RectOffset(edge, edge, edge, edge));
            Widgets.Fitter(rect);

            var body = Widgets.Fill("Body", rect, NativeSkin.PanelFill);
            var pad = Mathf.RoundToInt(S(0.7f));
            var content = body.rectTransform;
            Widgets.Column(content, S(0.25f), new RectOffset(pad, pad, pad, pad));

            BuildHeader(content);
            BuildBody(content);
        }

        private void BuildHeader(RectTransform parent)
        {
            var title = Widgets.Label("Title", parent, Skin, S(1.1f), NativeSkin.TextBright);
            title.text = Title;
            Widgets.Fixed(title.rectTransform, S(1.5f));

            _hint = Widgets.Label("Hint", parent, Skin, S(0.75f), NativeSkin.TextDim);
            Widgets.Fixed(_hint.rectTransform, S(1.1f));

            Divider(parent);
        }

        protected void Divider(RectTransform parent)
        {
            var line = Widgets.Fill("Divider", parent, NativeSkin.SlotEdge);
            Widgets.Fixed(line.rectTransform, Mathf.Max(1f, S(0.12f)));
        }
    }

    /// <summary>
    /// 창 자체. 게임의 <c>UIBase</c>를 상속하는 것이 요점이다 - 컨트롤 스택에 올라가면 게임이
    /// 여는 동안 캐릭터 조작을 막고(플레이어 입력이 <c>CurrentControlStack == null</c>을 본다)
    /// ESC 로 닫아 주며, 다른 창이 위에 열리면 우리 창을 알아서 비활성으로 내린다.
    /// </summary>
    internal sealed class PlannerPanel : UIBase
    {
        private bool _paused;

        // 도감처럼 타입 이름으로 찾는 게임 UI 목록에 우리 것을 끼워 넣을 이유가 없다.
        public override bool CanBeSearchedByTypeHash => false;

        /// <summary>
        /// 여는 동안 시간을 멈춘다. 게임에서 설정을 만지는 길은 ESC 일시정지 창을 거치는 것이라,
        /// 그때는 아래에서 일시정지 창이 이미 시간을 멈춰 두고 있다(<c>UI_PausePanel.OnOpened</c>가
        /// <c>GameTimeManager.Pause</c>를 부른다). 우리 창만 시간이 흐르면 만지는 사이에 얻어맞는다.
        /// 새로 주는 이득도 아니다 - ESC 로 언제든 멈출 수 있는 것이 게임 자신의 설계다.
        ///
        /// 멀티에서는 <c>GameTimeManager.Pause</c>가 스스로 아무것도 하지 않는다.
        /// </summary>
        public override void OnOpened()
        {
            base.OnOpened();

            // 이미 멈춰 있으면(일시정지 창이나 튜토리얼 팝업 위에서 열렸으면) 건드리지 않는다.
            // 우리가 닫으면서 남의 정지를 풀어 버리면 안 된다.
            if (GameTimeManager.Instance == null || Time.timeScale <= 0f) return;

            _paused = true;
            GameTimeManager.Instance.Pause();
        }

        public override void OnClosed()
        {
            base.OnClosed();
            if (!_paused) return;

            _paused = false;
            if (GameTimeManager.Instance != null) GameTimeManager.Instance.ResetTimeScaleTo1();
        }
    }
}
