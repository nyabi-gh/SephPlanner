using System;
using System.Collections.Generic;
using System.Text;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    internal enum PanelCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    /// <summary>
    /// 한 번 그리는 데 필요한 것 전부. 인자가 늘어날 때마다 서명을 고치는 대신 여기에 담는다.
    /// </summary>
    internal sealed class HudFrame
    {
        public GameSnapshot Snapshot;
        public Plan Plan;
        public ICatalog Catalog;
        public PluginPreferences Prefs;
        public CharmValueBook Values = CharmValueBook.Empty;
        public bool Expanded;
        public bool MixerOpen;
        public bool Recommendations = true;
        public bool MultiplayerAutoPlace;
        public bool QueryVerified;
        public PlanVerificationStatus RuntimeVerification;
        public string RuntimeVerificationReason = "";
        public string Hint = "";

        /// <summary>안내 줄이 지금 미리보기를 설명하고 있는가. 그때는 색이 달라야 눈에 든다.</summary>
        public bool HintIsPreview;

        /// <summary>보고 있는 후보의 <see cref="OfferAdvice.Key"/>. 빈 문자열이면 미리보기가 없다.</summary>
        public string PreviewKey = "";

        public int Gold => Snapshot?.Run?.Gold ?? 0;
    }

    /// <summary>
    /// 게임 HUD 캔버스 안에 직접 그리는 화면.
    ///
    /// 게임 UI 의 일부라서 전용 전체화면에서도 보이고, 게임 UI 배율을 따르며, 게임이 UI 를 감출 때
    /// 함께 감춰진다 - <c>UIManager.Hide</c>가 <c>UIRoot</c>의 CanvasGroup 알파를 0 으로 만드는데
    /// 우리가 그 밑에 달려 있기 때문이다.
    ///
    /// <b>크기는 전부 <see cref="NativeSkin.BaseSize"/>에 대한 비율이다.</b> HUD 캔버스가 픽셀
    /// 아트라 크게 확대돼 있어서, 화면 픽셀을 생각하고 숫자를 넣으면 그 배율만큼 어긋난다.
    ///
    /// <b>입력은 가져가지 않는다.</b> <c>UIBase</c>를 상속하지 않아 컨트롤 스택에 들어가지 않으므로
    /// ESC 와 키보드를 뺏지 않고, <see cref="Widgets"/>가 그리는 것마다 <c>raycastTarget</c>을 꺼서
    /// 마우스도 통과시킨다. 그래서 이 화면에는 누르는 것이 없고, 조작은 전부 단축키로 받는다.
    ///
    /// 다만 <b>커서 위치는 읽는다</b> - 화면을 옮길 때와 쪽지(<see cref="Tooltip"/>)를 띄울 때다.
    /// 읽기만 하는 것이라 게임에서 가져가는 입력은 여전히 없다.
    /// </summary>
    internal sealed class NativeHud
    {
        private const int MoveRows = 6;
        /// <summary>후보 목록에 보이는 줄 수. 미리보기 순환도 여기까지만 돈다 - 화면에 없는
        /// 후보로 넘어가면 무엇을 보고 있는지 알 수 없다.</summary>
        public const int OfferRows = 6;
        private const int MixRows = 3;

        /// <summary>
        /// 커서가 올라오면 설명할 것 하나. 그리는 자리마다 여기에 등록한다.
        ///
        /// <b>본문은 커서가 왔을 때 짓는다.</b> 화면에 있는 설명을 전부 미리 지어 두면 갱신마다
        /// 마흔 몇 개의 문장을 만들게 되는데, 그중 실제로 읽히는 것은 많아야 하나다.
        /// </summary>
        private struct HoverTarget
        {
            public RectTransform Rect;
            public string Title;
            public Func<string> Body;
        }

        /// <summary>
        /// 한 번 그린 근거. 이것이 그대로면 그린 결과도 그대로다.
        ///
        /// <see cref="Render"/>는 매 프레임 불리지만 내용이 바뀌는 것은 폴링마다 한 번이다.
        /// 프레임마다 격자와 목록을 다시 지으면 아무것도 달라지지 않은 프레임에서도 쓰레기가
        /// 쌓이고, 그것이 결국 게임을 세운다 - 이 게임은 증분 GC 가 프레임당 3ms 를 가져간다
        /// (<c>Sephiria_Data/boot.config</c>의 <c>gc-max-time-slice</c>).
        /// </summary>
        private readonly struct FrameKey
        {
            private readonly object _snapshot;
            private readonly object _plan;
            private readonly object _catalog;
            private readonly object _values;
            private readonly string _hint;
            private readonly string _reason;
            private readonly string _previewKey;
            private readonly PlanVerificationStatus _verification;
            private readonly int _prefs;
            private readonly bool _expanded;
            private readonly bool _recommendations;
            private readonly bool _queryVerified;
            private readonly bool _hintIsPreview;
            private readonly bool _multiplayerAutoPlace;

            public FrameKey(HudFrame frame)
            {
                _snapshot = frame.Snapshot;
                _plan = frame.Plan;
                _catalog = frame.Catalog;
                _values = frame.Values;
                _hint = frame.Hint;
                _reason = frame.RuntimeVerificationReason;
                _previewKey = frame.PreviewKey;
                _verification = frame.RuntimeVerification;

                // 빌드 창에서 강화 우선이나 밀고 있는 콤보를 바꾸면, 계획이 다시 풀리기 전에도
                // 격자와 칩의 표시가 달라진다. 개정 번호가 그 순간을 잡는다.
                _prefs = frame.Prefs != null ? frame.Prefs.Revision : -1;
                _expanded = frame.Expanded;
                _recommendations = frame.Recommendations;
                _queryVerified = frame.QueryVerified;
                _hintIsPreview = frame.HintIsPreview;

                // 멀티 동의 안내가 이 값으로 갈린다. 빠뜨리면 설정을 바꾼 직후 옛 문구가 남는다.
                _multiplayerAutoPlace = frame.MultiplayerAutoPlace;
            }

            public bool Matches(in FrameKey other) =>
                ReferenceEquals(_snapshot, other._snapshot) &&
                ReferenceEquals(_plan, other._plan) &&
                ReferenceEquals(_catalog, other._catalog) &&
                ReferenceEquals(_values, other._values) &&
                _verification == other._verification &&
                _prefs == other._prefs &&
                _expanded == other._expanded &&
                _recommendations == other._recommendations &&
                _queryVerified == other._queryVerified &&
                _hintIsPreview == other._hintIsPreview &&
                _multiplayerAutoPlace == other._multiplayerAutoPlace &&
                string.Equals(_hint, other._hint, StringComparison.Ordinal) &&
                string.Equals(_reason, other._reason, StringComparison.Ordinal) &&
                string.Equals(_previewKey, other._previewKey, StringComparison.Ordinal);
        }

        private NativeSkin _skin;
        private GameObject _root;
        private RectTransform _rect;
        private RectTransform _canvasRect;
        private Canvas _canvas;
        private CanvasGroup _group;
        private ContentSizeFitter _fitter;
        private float _width;
        private float _inner;
        private float _compactWidth;
        private float _compactInner;
        private bool _compact;
        private Vector2 _grab;

        /// <summary>기준 크기에 사용자 배율을 곱한 값. 화면의 모든 치수가 여기서 나온다.</summary>
        private float _base;

        private TextMeshProUGUI _hint;
        private LayoutElement _hintSize;
        private LayoutElement _chipSize;
        private TextMeshProUGUI _score;
        private TextMeshProUGUI _gain;
        private TextMeshProUGUI _notice;
        private LayoutElement _noticeSize;
        private TextMeshProUGUI _nextMove;
        private TextMeshProUGUI _waiting;
        private LayoutElement _waitingSize;

        private RectTransform _detail;
        private RectTransform _grid;
        private GridLayoutGroup _gridLayout;
        private readonly List<Cell> _cells = new List<Cell>();

        private Section _moves;
        private Section _offers;
        private Section _mixes;
        private Section _discards;
        private TextMeshProUGUI _offerNotice;
        private TextMeshProUGUI _chips;

        private readonly Tooltip _tooltip = new Tooltip();
        private readonly List<HoverTarget> _hover = new List<HoverTarget>();
        private bool _hoverable;

        /// <summary>마지막으로 그린 근거와, 그린 것이 있는지.</summary>
        private FrameKey _drawn;
        private bool _hasDrawn;

        /// <summary>지금 화면에 떠 있는 안내문. null 이면 안내문이 아니라 계획을 그리고 있다.</summary>
        private string _noticeDrawn;

        public string Origin { get; private set; } = "";
        public string Blocker { get; private set; } = "";
        public bool IsAlive => _root != null;

        public bool TryCreate(PanelCorner corner, Vector2 margin, float widthScale, float scale)
        {
            if (IsAlive) return true;
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

            _skin = NativeSkin.Borrow(root);
            _base = _skin.BaseSize * Mathf.Clamp(scale, PluginSettings.MinScale, PluginSettings.MaxScale);
            Build(
                root, corner, new Vector2(Mathf.Max(0f, margin.x), Mathf.Max(0f, margin.y)),
                Mathf.Clamp(widthScale, PluginSettings.MinWidth, PluginSettings.MaxWidth));
            Origin = _skin.Origin;
            return true;
        }

        private float S(float ratio) => _base * ratio;

        private void Build(UIRoot root, PanelCorner corner, Vector2 margin, float widthScale)
        {
            // 캔버스가 밖에서 파괴되면 Destroy() 를 거치지 않는다. 죽은 셀을 재사용하면 안 된다.
            _cells.Clear();
            _hover.Clear();

            // 새로 지은 화면은 비어 있다. 직전에 그린 근거를 그대로 두면, 같은 계획이라는 이유로
            // 아무것도 그리지 않고 빈 화면이 남는다.
            _hasDrawn = false;
            _noticeDrawn = null;

            // 장미빛 테두리 한 겹과 그 안의 어두운 속.
            var frame = Widgets.Fill("SephPlannerHud", root.transform, NativeSkin.Frame);
            _root = frame.gameObject;

            // 다음에 크기를 빌릴 때 우리 글자를 세지 않도록 표시해 둔다.
            _root.AddComponent<SephPlannerWidget>();
            _canvas = root.Canvas;
            _canvasRect = (RectTransform)root.transform;

            // 게임 창들 위에 그린다. 보상 창과 레벨업 창이 우리보다 위 캔버스에 있어서, 이것이
            // 없으면 무엇을 집을지 고르는 바로 그 순간에 화면이 덮여 보이지 않는다.
            Widgets.Layer(_root, Layers.Hud, clickable: false);

            // 불투명도 전용. UIRoot 자신의 CanvasGroup 은 게임이 UI 를 감출 때 쓰므로 건드리지 않는다.
            _group = _root.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            var rect = frame.rectTransform;
            _rect = rect;
            Place(rect, corner, new Vector2(S(margin.x), S(margin.y)));
            _width = S(widthScale);
            _compact = false;
            rect.sizeDelta = new Vector2(_width, 0f);

            var edge = Mathf.Max(1, Mathf.RoundToInt(S(0.25f)));
            Widgets.Column(rect, 0f, new RectOffset(edge, edge, edge, edge));

            // 높이는 내용이 정한다. 층층이 붙이면 서로 다투므로 맨 바깥에만 둔다.
            _fitter = Widgets.Fitter(rect);

            var body = Widgets.Fill("Body", rect, NativeSkin.PanelFill);
            var pad = Mathf.RoundToInt(S(0.6f));

            // 안쪽에 실제로 쓸 수 있는 폭. 격자와 오른쪽 열이 이 값을 넘으면 테두리 밖으로
            // 삐져나가 화면이 깨진다 - 폭을 좁게 잡았을 때 실제로 그랬다.
            _inner = _width - 2f * (edge + pad);

            // 접으면 판이 좁아진다. 그 폭으로 재지 않으면 안내 줄이 서너 줄로 접히면서 할당된
            // 높이를 넘어 게임 화면 위로 흘러내린다.
            _compactWidth = Mathf.Min(_width, S(18f));
            _compactInner = _compactWidth - 2f * (edge + pad);
            var content = body.rectTransform;
            Widgets.Column(content, S(0.2f), new RectOffset(pad, pad, pad, pad));

            BuildHeader(content);
            // 경고는 여러 개가 한꺼번에 걸린다. 한 줄짜리로 두면 첫 것만 보이고, 멀티 안내처럼
            // 목록 끝에 있는 것은 다른 경고가 하나라도 있으면 영영 보이지 않는다.
            _notice = Widgets.Paragraph("Notice", content, _skin, S(0.8f), NativeSkin.Amber);
            _noticeSize = Widgets.Fixed(_notice.rectTransform, S(0.8f) * 1.4f);
            _nextMove = Line(content, S(0.95f), NativeSkin.Text);

            // 안내문은 접힌 판에 뜨는데 접힌 폭이 좁다. 한 줄짜리로 두면 "데이터 생성 실패 -
            // F9 로 다시 시도하세요(로그에 이유가 있습니다)" 같은 문장이 앞머리만 남고 잘린다.
            _waiting = Widgets.Paragraph("Waiting", content, _skin, S(0.95f), NativeSkin.Text);
            _waitingSize = Widgets.Fixed(_waiting.rectTransform, S(0.95f) * 1.4f);

            _detail = Widgets.Rect("Detail", content);
            Widgets.Column(_detail, S(0.3f));

            var detail = Mathf.Min(S(12f), _inner * 0.55f);
            BuildGrid(_detail);
            _moves = new Section(_detail, _skin, _base, "옮길 것", NativeSkin.TextDim, MoveRows, detail);
            BuildOffers(_detail, detail);
            _mixes = new Section(_detail, _skin, _base, "석판 합성기", NativeSkin.Mint, MixRows, detail);
            _discards = new Section(_detail, _skin, _base, "하나 빼기 검토 · 자동 제거 안 함", NativeSkin.Mint, 3, detail);
            _chips = Widgets.Paragraph("Chips", _detail, _skin, S(0.8f), NativeSkin.TextDim);
            _chips.richText = true;
            _chipSize = Widgets.Fixed(_chips.rectTransform, S(1.1f));

            _tooltip.Create(root, _skin, _base);
        }

        private static void Place(RectTransform rect, PanelCorner corner, Vector2 margin)
        {
            var right = corner == PanelCorner.TopRight || corner == PanelCorner.BottomRight;
            var top = corner == PanelCorner.TopLeft || corner == PanelCorner.TopRight;

            var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = new Vector2(right ? -margin.x : margin.x, top ? -margin.y : margin.y);
        }

        public void SetOpacity(float alpha)
        {
            if (_group != null) _group.alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>
        /// 옮기기 시작한 자리를 기억한다. 이것이 없으면 잡는 순간 화면 모서리가 커서로 튄다.
        /// </summary>
        public void BeginDrag(Vector2 screenPoint)
        {
            if (IsAlive) _grab = Anchored(screenPoint) - _rect.anchoredPosition;
        }

        public void DragTo(Vector2 screenPoint)
        {
            if (IsAlive) _rect.anchoredPosition = ClampToCanvas(Anchored(screenPoint) - _grab);
        }

        /// <summary>지금 자리를 설정에 적어 둘 값으로. 모서리에서 안쪽으로 얼마인지를 기준 크기 단위로 센다.</summary>
        public Vector2 Margin => IsAlive
            ? new Vector2(
                (_rect.anchorMin.x > 0.5f ? -_rect.anchoredPosition.x : _rect.anchoredPosition.x) / _base,
                (_rect.anchorMin.y > 0.5f ? -_rect.anchoredPosition.y : _rect.anchoredPosition.y) / _base)
            : Vector2.zero;

        private Vector2 Anchored(Vector2 screenPoint)
        {
            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screenPoint, camera, out var local);

            var anchor = new Vector2(
                _rect.anchorMin.x > 0.5f ? _canvasRect.rect.xMax : _canvasRect.rect.xMin,
                _rect.anchorMin.y > 0.5f ? _canvasRect.rect.yMax : _canvasRect.rect.yMin);
            return local - anchor;
        }

        private Vector2 ClampToCanvas(Vector2 position)
        {
            var inset = S(0.25f);
            var canvas = _canvasRect.rect.size;
            var panel = _rect.rect.size;

            var minX = _rect.anchorMin.x > 0.5f ? -canvas.x + panel.x + inset : inset;
            var maxX = _rect.anchorMin.x > 0.5f ? -inset : canvas.x - panel.x - inset;
            var minY = _rect.anchorMin.y > 0.5f ? -canvas.y + panel.y + inset : inset;
            var maxY = _rect.anchorMin.y > 0.5f ? -inset : canvas.y - panel.y - inset;

            position.x = minX <= maxX ? Mathf.Clamp(position.x, minX, maxX) : 0f;
            position.y = minY <= maxY ? Mathf.Clamp(position.y, minY, maxY) : 0f;
            return position;
        }

        private void BuildHeader(RectTransform parent)
        {
            var row = Widgets.Rect("Header", parent);
            Widgets.Row(row, S(0.5f));
            Widgets.Fixed(row, S(1.5f));

            _score = Widgets.Label("Score", row, _skin, S(1.2f), NativeSkin.TextBright);
            Widgets.Fixed(_score.rectTransform, S(1.5f)).flexibleWidth = 1;

            _gain = Widgets.Label("Gain", row, _skin, S(0.9f), NativeSkin.Good, TextAlignmentOptions.MidlineRight);
            Widgets.Fixed(_gain.rectTransform, S(1.5f), S(4f));

            // 안내 줄은 접혔다 펴지며 길이가 크게 달라진다. 한 줄로 잘라 내면 뒤쪽 단축키가
            // 통째로 사라져, 누를 것이 없는 화면에서 조작을 알 길이 없어진다.
            _hint = Widgets.Paragraph("Hint", parent, _skin, S(0.75f), NativeSkin.TextDim);
            _hintSize = Widgets.Fixed(_hint.rectTransform, S(1f));
        }

        private void BuildGrid(RectTransform parent)
        {
            _grid = Widgets.Rect("Grid", parent);
            _gridLayout = _grid.gameObject.AddComponent<GridLayoutGroup>();
            _gridLayout.spacing = new Vector2(S(0.15f), S(0.15f));
            _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _gridLayout.constraintCount = GridSpec.DefaultWidth;
            SizeCells(GridSpec.DefaultWidth);
        }

        /// <summary>
        /// 칸 크기는 폭에서 나온다. 고정 크기로 두면 폭을 좁게 잡았을 때 격자가 테두리를 뚫고
        /// 나간다. 넓을 때까지 따라 커지지는 않게 원래 크기를 상한으로 둔다.
        /// </summary>
        private void SizeCells(int columns)
        {
            columns = Mathf.Max(1, columns);
            var spacing = _gridLayout.spacing.x;
            var cell = Mathf.Min(S(3.4f), (_inner - spacing * (columns - 1)) / columns);
            if (cell <= 0f) cell = S(3.4f);

            _gridLayout.cellSize = new Vector2(cell, cell * (2.6f / 3.4f));
        }

        private void BuildOffers(RectTransform parent, float detail)
        {
            _offers = new Section(
                parent, _skin, _base, "지금 집을 수 있는 것", NativeSkin.Mint, OfferRows, detail);

            // 순위를 그대로 믿으면 안 된다는 것은 늘 보여야 한다. 머리글 바로 밑이라야 목록을
            // 읽기 전에 눈에 들어온다.
            _offerNotice = Line(_offers.Body, S(0.75f), NativeSkin.TextDim);
            _offerNotice.transform.SetSiblingIndex(1);
        }

        private TextMeshProUGUI Line(RectTransform parent, float size, Color color)
        {
            var label = Widgets.Label("Line", parent, _skin, size, color);
            Widgets.Fixed(label.rectTransform, size * 1.4f);
            return label;
        }

        /// <summary>런 밖처럼 보여 줄 배치가 없을 때. 직전 런의 점수를 남겨 두면 거짓말이 된다.</summary>
        public void RenderNotice(string message)
        {
            if (!IsAlive) return;
            if (_noticeDrawn == message) return;

            _noticeDrawn = message;
            FrameCost.CountDraw();

            // 안내문이 계획 화면을 덮었다. 다음에 계획을 그릴 때는 처음부터 다시 그려야 한다.
            _hasDrawn = false;

            _score.text = "SephPlanner";
            _gain.text = "";
            _hint.text = "";
            _nextMove.text = "";
            _waiting.text = message;
            Widgets.FitHeight(_waiting, _waitingSize, _compactInner);
            Widgets.SetActive(_hint, false);
            Widgets.SetActive(_nextMove, false);
            Widgets.SetActive(_waiting, message.Length > 0);
            Widgets.SetActive(_notice, false);
            Widgets.SetActive(_detail, false);
            SetCompact(true);

            _hover.Clear();
            _hoverable = false;
            _tooltip.Hide();
        }

        /// <summary>
        /// 그린 것을 없던 일로 한다. 그리다 예외가 나면 화면은 반쯤 그려진 채로 남는데, 근거가
        /// 그대로면 다음 프레임이 "이미 그렸다"며 물러서서 그 상태가 굳는다.
        /// </summary>
        public void Invalidate()
        {
            _hasDrawn = false;
            _noticeDrawn = null;
        }

        public void Render(HudFrame frame)
        {
            if (!IsAlive) return;

            // 매 프레임 불리지만 내용이 바뀌는 것은 폴링마다 한 번이다. 근거가 그대로면 그린 것도
            // 그대로이므로, 같은 글자를 다시 짓지 않고 물러선다.
            var key = new FrameKey(frame);
            if (_hasDrawn && key.Matches(_drawn)) return;

            _drawn = key;
            _hasDrawn = true;
            _noticeDrawn = null;
            FrameCost.CountDraw();

            var snapshot = frame.Snapshot;
            var plan = frame.Plan;
            var expanded = frame.Expanded;

            // 접힌 판은 좁다. 접을 참이면 접힌 폭으로 재야 넘치지 않는다.
            var inner = expanded ? _inner : _compactInner;

            _hint.text = frame.Hint;
            _hint.color = frame.HintIsPreview ? NativeSkin.Mint : NativeSkin.TextDim;
            Widgets.FitHeight(_hint, _hintSize, inner);
            Widgets.SetActive(_hint, frame.Hint.Length > 0);
            Widgets.SetActive(_waiting, false);

            _hover.Clear();
            _hoverable = expanded;

            var previewed = Previewed(plan, frame.PreviewKey);
            var score = previewed?.Preview.Score ?? plan.Best.Score;
            var gain = score - plan.Current.Score;
            _score.text = previewed != null
                ? $"예상 DPS · 미리보기 {score:0.#}"
                : $"예상 DPS {plan.Current.Score:0.#} → {score:0.#}";
            _gain.text = gain > 0.001 ? $"+{gain:0.#}" : gain < -0.001 ? $"{gain:0.#}" :
                previewed == null && plan.HasPlacementChanges ? "배치 정리" : "변경 없음";
            _gain.color = gain > 0.001 ? NativeSkin.Good : gain < -0.001 ? NativeSkin.Bad : NativeSkin.TextDim;
            if (previewed == null && plan.HasPlacementChanges)
            {
                if (plan.Best.UnpreservedCharms.Count < plan.Current.UnpreservedCharms.Count)
                {
                    _gain.text = $"활성 보존 우선 ({gain:+0.#;-0.#;0})";
                    _gain.color = NativeSkin.Mint;
                }
                else if (Math.Abs(gain) <= 0.001 && plan.Best.UnsafeEmptyCells < plan.Current.UnsafeEmptyCells)
                {
                    _gain.text = "감점 칸 정리";
                    _gain.color = NativeSkin.Mint;
                }
            }
            if (previewed == null && plan.HasPlacementChanges &&
                (plan.Best.PriorityComboMatches > plan.Current.PriorityComboMatches ||
                 plan.Best.PriorityComboProgress > plan.Current.PriorityComboProgress))
            {
                _gain.text = $"지정 콤보 우선 ({gain:+0.#;-0.#;0})";
                _gain.color = NativeSkin.Mint;
            }

            if (previewed == null && plan.HasPlacementChanges &&
                plan.Best.UnretainedCharms.Count < plan.Current.UnretainedCharms.Count)
            {
                _gain.text = $"사용 유지 우선 ({gain:+0.#;-0.#;0})";
                _gain.color = NativeSkin.Mint;
            }

            var warning = Warning(
                snapshot, plan, frame.MultiplayerAutoPlace, frame.QueryVerified,
                frame.RuntimeVerification, frame.RuntimeVerificationReason);
            var combat = previewed?.Preview.Combat ?? plan.Best.Combat;
            if (combat != null && combat.Unsupported.Count > 0)
                warning += (warning.Length > 0 ? "\n" : "") + $"추정·미반영 {combat.Unsupported.Count}항목 · F2 → DPS 내역";
            if (previewed == null && plan.UnsupportedChangeWarnings.Count > 0)
                warning += (warning.Length > 0 ? "\n" : "") + (plan.AllowUnsupportedChanges ? "미지원 변경 자동 적용 허용됨" : "미지원 변경으로 자동 적용 제한") + " · F2 → DPS 내역";
            _notice.text = warning;
            Widgets.FitHeight(_notice, _noticeSize, inner);
            Widgets.SetActive(_notice, warning.Length > 0);

            // 접었을 때는 지금 옮길 것 하나만. 펼치면 아래 목록이 그 일을 하므로 겹치지 않게 접는다.
            var next = previewed == null && plan.Moves.Count > 0 ? plan.Moves[0] : null;
            _nextMove.text = next == null ? "" : $"{next.Label}  {next.Detail}";
            Widgets.SetActive(_nextMove, !expanded && next != null);

            Widgets.SetActive(_detail, expanded);
            SetCompact(!expanded);
            if (!expanded)
            {
                _tooltip.Hide();
                return;
            }

            RenderGrid(snapshot, plan, previewed?.Preview, frame);
            if (previewed == null) RenderMoves(plan);
            else
            {
                _moves.Begin();
                _moves.Add("획득 후 예상 배치", previewed.Candidate.Name, NativeSkin.Mint);
                _moves.Add("자동 배치", "미리보기를 종료한 뒤 실행하세요.", NativeSkin.TextDim);
                _moves.End();
            }
            RenderOffers(plan, frame, previewed);
            RenderMixes(plan, snapshot.Mixer, frame.MixerOpen);
            RenderDiscards(plan, previewed == null);
            RenderChips(snapshot, frame);
        }

        private static OfferAdvice Previewed(Plan plan, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            foreach (var advice in plan.Offers)
            {
                if (advice.Key == key && advice.Preview != null) return advice;
            }
            return null;
        }

        /// <summary>
        /// 커서가 무엇 위에 있는지 보고 쪽지를 띄운다. 펼쳐져 있을 때만 센다 - 접힌 화면은 곁눈질용
        /// 한 줄이고, 그때 쪽지가 뜨면 플레이를 가린다.
        /// </summary>
        public void UpdateHover(Vector2 cursor, bool enabled)
        {
            if (!IsAlive || !_tooltip.IsAlive) return;
            if (!enabled || !_hoverable || !_root.activeSelf || _group.alpha <= 0.01f)
            {
                _tooltip.Hide();
                return;
            }

            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            foreach (var target in _hover)
            {
                if (target.Rect == null || !target.Rect.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(target.Rect, cursor, camera)) continue;

                var body = target.Body != null ? target.Body() : "";

                // 할 말이 없으면 띄우지 않는다. 빈 쪽지가 뜨면 커서를 옮길 때마다 빈 상자가
                // 깜빡인다. 그려 둔 자리는 서로 겹치지 않으므로 여기서 끝내면 된다.
                if (target.Title.Length == 0 && body.Length == 0) break;

                _tooltip.Show(target.Title, body, cursor);
                return;
            }
            _tooltip.Hide();
        }

        /// <summary>
        /// 커서가 오면 무엇을 띄울지 등록한다. <b>본문은 그때 짓는다</b> - 화면에 있는 설명을
        /// 전부 미리 지어 두면, 읽히지도 않을 마흔 몇 개의 문장을 갱신마다 만들게 된다.
        /// 할 말이 없는 자리는 <see cref="UpdateHover"/>가 걸러낸다.
        /// </summary>
        private void Hover(RectTransform rect, string title, Func<string> body)
        {
            if (rect == null) return;

            _hover.Add(new HoverTarget
            {
                Rect = rect,
                Title = title,
                Body = body,
            });
        }

        /// <summary>
        /// 지금 화면에서 알려야 할 것. 점수를 믿을 수 없는 상황이
        /// 멀티 안내보다 먼저다.
        /// </summary>
        private static string Warning(
            GameSnapshot snapshot, Plan plan, bool multiplayerAutoPlace, bool queryVerified,
            PlanVerificationStatus runtimeVerification, string runtimeVerificationReason)
        {
            var warnings = new List<string>();
            if (!queryVerified)
                warnings.Add("석판 질의 검증이 끝나지 않았거나 실패해 자동 배치를 껐습니다. F9로 다시 만드세요.");

            if (plan.Best.UnplacedTablets > 0)
                warnings.Add($"석판 {plan.Best.UnplacedTablets}개는 놓을 자리가 없어 계산에서 빠졌습니다.");

            if (!plan.Verification.Passed)
                warnings.Add(plan.Verification.Reason);

            if (runtimeVerification != PlanVerificationStatus.Passed &&
                runtimeVerificationReason.Length > 0 &&
                runtimeVerificationReason != plan.Verification.Reason)
                warnings.Add(runtimeVerificationReason);

            if (!plan.ManualMoveInstructionsAvailable)
                warnings.Add("목표 배치가 유효하지 않아 수동 이동 순서를 만들 수 없습니다.");

            if (plan.SkippedOffers > 0)
                warnings.Add($"선택지가 많아 {plan.SkippedOffers}개는 평가하지 못했습니다.");

            if (plan.Best.UnheldCharms.Count > 0)
                warnings.Add($"배치 조건을 무시하는 칸이 모자라 고정 {plan.Best.UnheldCharms.Count}개를 지키지 못했습니다.");

            warnings.AddRange(plan.ComboPlacementWarnings);
            warnings.AddRange(plan.RetentionWarnings);
            warnings.AddRange(plan.SupportWarnings);
            warnings.AddRange(plan.ActivationWarnings);
            warnings.AddRange(plan.PositionWarnings);

            if (snapshot.IsMultiplayer)
            {
                warnings.Add(multiplayerAutoPlace
                    ? "멀티플레이 세션 - 자동 배치 허용됨 (실험). 같이 하는 사람의 동의를 받으세요."
                    : "멀티플레이 세션 - 제안만 표시합니다.");
            }
            return string.Join("\n", warnings);
        }

        /// <summary>
        /// 격자를 그린다. 미리보기를 고르면 그 후보를 집었을 때의 배치를 대신 그린다 - 솔버가
        /// 후보마다 이미 푼 결과라 여기서 다시 계산하지 않는다.
        /// </summary>
        private void RenderGrid(GameSnapshot snapshot, Plan plan, PlanPreview preview, HudFrame frame)
        {
            var inventory = snapshot.Inventory;
            var total = inventory.Width * inventory.Height;
            _gridLayout.constraintCount = inventory.Width;
            SizeCells(inventory.Width);

            while (_cells.Count < total) _cells.Add(new Cell(_grid, _skin, _base));
            for (var i = total; i < _cells.Count; i++) _cells[i].Hide();

            var tablets = preview != null ? preview.Tablets : plan.Best.Tablets;
            var levels = preview != null ? preview.Levels : plan.Best.Levels;
            var effectiveLevels = preview != null ? preview.EffectiveLevels : plan.Best.EffectiveLevels;
            var inactiveCells = preview != null ? preview.InactiveCells : plan.Best.InactiveCells;
            var names = preview != null ? preview.Names : plan.Names;
            var charms = preview != null ? preview.Charms : plan.Charms;

            var tabletCells = new Dictionary<GridPos, TabletPlacement>();
            foreach (var placement in tablets) tabletCells[placement.Position] = placement;

            // 평소에는 옮겨야 할 자리를, 미리보기에서는 달라지는 자리를 같은 금색 테두리로 짚는다.
            var marked = new HashSet<GridPos>();
            if (preview != null) marked.UnionWith(preview.Changed);
            else foreach (var move in plan.Moves) marked.Add(move.To);

            for (var index = 0; index < total; index++)
            {
                var cell = _cells[index];
                var position = new GridPos(index % inventory.Width, index / inventory.Width);
                cell.Show();

                if (index >= inventory.Storage)
                {
                    cell.SetClosed();
                }
                else if (tabletCells.TryGetValue(position, out var tablet))
                {
                    var name = Naming.OfTablet(tablet);
                    var rotation = tablet.Rotation;
                    cell.SetTablet(
                        name, rotation, marked.Contains(position),
                        IconOf(tablet.Definition.EntityId));
                    Hover(cell.Rect, name, () => "회전 " + rotation * 90 + "°");
                }
                else if (levels.TryGetValue(position, out var level))
                {
                    effectiveLevels.TryGetValue(position, out var effective);
                    inactiveCells.TryGetValue(position, out var reason);
                    names.TryGetValue(position, out var name);
                    charms.TryGetValue(position, out var charmId);

                    var pinned = 0;
                    cell.SetCharm(
                        name ?? "", level, effective, reason, marked.Contains(position),
                        IconOf(charmId), pinned);
                    HoverCell(cell, name ?? "", level, effective, reason, charmId, pinned, frame);
                }
                else
                {
                    cell.SetEmpty();
                }
            }
        }

        private void HoverCell(
            Cell cell, string name, int level, int effective, CharmInactiveReason reason,
            int charmId, int pinned, HudFrame frame)
        {
            var definition = frame.Catalog != null ? frame.Catalog.Charm(charmId) : null;
            var values = frame.Values;

            Hover(cell.Rect, name, () =>
            {
                var lines = Explain.Cell(name, level, effective, reason, definition, values, combat: true);

                // 첫 줄은 쪽지의 제목으로 올라간다.
                lines.RemoveAt(0);
                if (pinned > 0)
                {
                    lines.Add(
                        $"강화 우선 {new string('★', pinned)} - 이득을 " +
                        $"{PlanPreferences.WeightOf(pinned):0.##}배로 칩니다. 패널티는 그대로 반영합니다.");
                }
                else if (pinned < 0)
                {
                    lines.Add(
                        $"양보 {string.Concat(System.Linq.Enumerable.Repeat(_skin.YieldMark, -pinned))} - 이득을 " +
                        $"{PlanPreferences.WeightOf(pinned):0.##}배로 칩니다. 패널티는 그대로 반영합니다.");
                }
                if (frame.Prefs != null && frame.Prefs.IsRetained(charmId))
                    lines.Add("사용 유지: 활성 상태와 모래시계의 마법 연결을 우선하며 빼기·교체 추천에서 보호합니다.");
                if (frame.Prefs != null && frame.Prefs.IsHeld(charmId))
                    lines.Add("고정 - 배치 조건을 무시하는 칸에만 앉힙니다.");
                return Explain.Join(lines);
            });
        }

        /// <summary>
        /// 게임이 들고 있는 스프라이트를 그대로 쓴다. 원본이 이미 메모리에 있다.
        /// </summary>
        private static Sprite IconOf(int entityId)
        {
            if (entityId == 0) return null;

            var entity = ItemDatabase.FindItemById(entityId);
            return entity != null ? entity.icon : null;
        }

        private void RenderMoves(Plan plan)
        {
            _moves.Begin();
            if (!plan.ManualMoveInstructionsAvailable)
            {
                _moves.Add("수동 이동 불가", "목표 배치를 다시 계산해야 합니다.", NativeSkin.Amber);
                _moves.End();
                return;
            }
            for (var i = 0; i < plan.Moves.Count && i < MoveRows; i++)
            {
                var move = plan.Moves[i];
                var last = i == MoveRows - 1 && plan.Moves.Count > MoveRows;
                _moves.Add(
                    last ? $"… 외 {plan.Moves.Count - MoveRows + 1}개" : move.Label,
                    last ? "" : move.Detail,
                    NativeSkin.Text);
            }
            _moves.End();
        }

        private void RenderOffers(Plan plan, HudFrame frame, OfferAdvice previewed)
        {
            _offers.Begin();

            var guessed = 0;
            for (var i = 0; i < plan.Offers.Count && i < OfferRows; i++)
            {
                var advice = plan.Offers[i];
                var charm = advice.Candidate.Charm;

                if (advice.Preview?.Combat?.Unsupported.Count > 0) guessed++;

                var picked = previewed != null && previewed.Key == advice.Key;
                var row = _offers.Add(
                    (picked ? "> " : "") + advice.Candidate.Name, Detail(advice),
                    picked ? NativeSkin.GoldEdge : NameTone(advice));
                var gold = frame.Gold;
                var values = frame.Values;
                Hover(row, advice.Candidate.Name, () => Explain.Join(Explain.Offer(advice, gold, values)));
            }
            _offers.End();

            _offerNotice.text = guessed > 0
                ? $"예상 DPS · {guessed}개 후보에 추정·미반영 효과가 있습니다. F2 → DPS 내역"
                : "선택한 전투 조건과 추천 기준으로 비교한 예상 DPS입니다.";
            Widgets.SetActive(_offerNotice, plan.Offers.Count > 0);
        }

        private static Color NameTone(OfferAdvice advice) =>
            !advice.Available || !advice.Affordable ? NativeSkin.TextDim
            : advice.MatchesPriority || advice.MatchesPreset ? NativeSkin.Mint
            : NativeSkin.Text;

        /// <summary>
        /// 한 줄 오른쪽에 붙는 것들. 색이 서로 다르므로 TMP 서식으로 칠한다 - 텍스트를 여럿으로
        /// 쪼개 배치하는 것보다 짧고, 폭이 바뀌어도 알아서 붙는다.
        /// </summary>
        private static string Detail(OfferAdvice advice)
        {
            var parts = new StringBuilder();

            if (!advice.Available) return Tint("선택 불가", NativeSkin.Bad);

            if (advice.MatchesPreset) parts.Append(Tint("빌드", NativeSkin.Mint)).Append("  ");

            if (advice.ComboText.Length > 0)
            {
                parts.Append(Tint(
                        advice.ComboText,
                        advice.ComboCompletes ? NativeSkin.Good : advice.ComboLoses ? NativeSkin.Bad : NativeSkin.Mint))
                     .Append("  ");
            }

            var reach = Explain.Reach(advice.Effect);
            if (reach.Length > 0) parts.Append(Tint(reach, NativeSkin.TextDim)).Append("  ");

            if (advice.Candidate.Price > 0)
            {
                parts.Append(Tint(
                    advice.Candidate.Price + "골드",
                    advice.Affordable ? NativeSkin.TextDim : NativeSkin.Bad)).Append("  ");
            }

            var gain = advice.Gain;
            var text = (gain > 0.001 ? $"+{gain:0.#}" : gain < -0.001 ? $"{gain:0.#}" : "0") + " DPS";
            parts.Append(Tint(
                text, gain > 0.001 ? NativeSkin.Good : gain < -0.001 ? NativeSkin.Bad : NativeSkin.TextDim));
            return parts.ToString();
        }

        private void RenderDiscards(Plan plan, bool visible)
        {
            _discards.Begin();
            foreach (var advice in plan.Discards)
            {
                var row = _discards.Add(advice.Name, $"제외 후 재배치 +{advice.Gain:0.#} DPS", NativeSkin.Text);
                Hover(row, advice.Name, () => Explain.Join(Explain.Discard(advice)));
            }
            _discards.End();
            Widgets.SetActive(_discards.Root, visible && plan.Discards.Count > 0);
        }

        private void RenderMixes(Plan plan, MixerState mixer, bool mixerOpen)
        {
            _mixes.Begin();
            for (var i = 0; i < plan.Mixes.Count && i < MixRows; i++)
            {
                var advice = plan.Mixes[i];
                var name = advice.NameA + " + " + advice.NameB;
                var row = _mixes.Add(
                    name,
                    RotationTag(advice) + Tint($"{advice.Gain:+0.#;-0.#;0} DPS",
                        advice.Gain > 0.001 ? NativeSkin.Good : NativeSkin.TextDim),
                    advice.Affordable ? NativeSkin.Text : NativeSkin.TextDim);
                Hover(row, name, () => Explain.Join(Explain.Mix(advice)));
            }
            if (mixerOpen && plan.Mixes.Count == 0)
                _mixes.Add("추천 조합 없음", mixer == null ? "합성기 정보를 읽는 중입니다." :
                    mixer.Used ? "이 합성기는 이미 사용했습니다." : "현재 석판에서 추천할 수 있는 조합을 찾지 못했습니다.", NativeSkin.TextDim);
            _mixes.End();

            Widgets.SetActive(_mixes.Root, mixerOpen || mixer != null && !mixer.Used && plan.Mixes.Count > 0);
        }

        /// <summary>
        /// 합성기에 넣기 전에 맞춰 두어야 하는 회전. 사람이 손으로 돌려야 하는 일이라 빠뜨리면
        /// 답이 반쪽이 된다. 줄에 붙는 짧은 표이고, 어느 석판을 몇 도 돌리는지는
        /// <see cref="Explain.Turn"/>가 쪽지에서 말한다.
        /// </summary>
        private static string RotationTag(MixAdvice advice)
        {
            if (advice.RotationA == 0 && advice.RotationB == 0) return "";

            return Tint($"회전 {advice.RotationA}/{advice.RotationB}", NativeSkin.Amber) + "  ";
        }

        /// <summary>
        /// 지금 걸려 있는 콤보. 밀고 있는 빌드로 지정한 것은 앞에 점을 찍는다 - 지정은 빌드
        /// 창에서 하고, 여기는 그것이 실제로 걸려 있는지를 보여주는 자리다.
        ///
        /// 이름은 카탈로그에서 가져온다. 내부 식별자를 그대로 띄우면 무엇인지 알 수 없다.
        /// </summary>
        private void RenderChips(GameSnapshot snapshot, HudFrame frame)
        {
            var counts = snapshot.Inventory?.ComboCounts;
            if (counts == null || counts.Count == 0)
            {
                Widgets.SetActive(_chips, false);
                return;
            }

            // 사전의 순서는 물건을 옮기면 바뀐다. 줄이 매번 뒤섞이지 않도록 여기서 고정한다.
            var ids = new List<string>();
            foreach (var pair in counts)
            {
                if (pair.Value > 0) ids.Add(pair.Key);
            }
            ids.Sort((a, b) =>
            {
                var byCount = counts[b].CompareTo(counts[a]);
                return byCount != 0 ? byCount : string.CompareOrdinal(a, b);
            });

            var marks = frame.Recommendations && frame.Prefs != null;
            var text = new StringBuilder();
            foreach (var id in ids)
            {
                var combo = frame.Catalog != null ? frame.Catalog.Combo(id) : null;
                var name = combo != null ? Naming.Of(combo.Names, combo.Id, id) : id;
                var priority = marks && frame.Prefs.IsPriority(id);

                if (text.Length > 0) text.Append("  ");
                text.Append(Tint(
                    (priority ? "● " : "") + name + " " + counts[id],
                    priority ? NativeSkin.Mint : NativeSkin.TextDim));
            }

            _chips.text = text.ToString();
            Widgets.FitHeight(_chips, _chipSize, _inner);
            Widgets.SetActive(_chips, text.Length > 0);
        }

        /// <summary>
        /// 접었을 때는 폭을 제한한다. 긴 안내나 경고가 있어도 펼친 폭으로 커지지 않아야 한다.
        /// </summary>
        private void SetCompact(bool compact)
        {
            if (_fitter == null || _compact == compact) return;

            _compact = compact;
            _fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            _rect.sizeDelta = new Vector2(compact ? _compactWidth : _width, _rect.sizeDelta.y);
        }

        public void SetVisible(bool visible)
        {
            if (!IsAlive) return;
            if (_root.activeSelf != visible) _root.SetActive(visible);
            if (visible) _rect.anchoredPosition = ClampToCanvas(_rect.anchoredPosition);

            // 쪽지는 HUD 밖(캔버스 바로 밑)에 달려 있어서 함께 꺼지지 않는다. 숨긴 화면 옆에
            // 쪽지만 남으면 어디서 나온 것인지 알 수 없다.
            if (!visible) _tooltip.Hide();
        }

        public void Destroy()
        {
            _tooltip.Destroy();
            if (_root != null) UnityEngine.Object.Destroy(_root);

            _root = null;
            _cells.Clear();
            _hover.Clear();
            _hasDrawn = false;
            _noticeDrawn = null;
        }

        internal static string Tint(string text, Color color) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

        /// <summary>머리글 하나와 줄 몇 개. 줄은 미리 만들어 두고 쓰는 만큼만 켠다.</summary>
        private sealed class Section
        {
            private readonly List<(TextMeshProUGUI Label, TextMeshProUGUI Detail)> _rows =
                new List<(TextMeshProUGUI, TextMeshProUGUI)>();
            private int _used;

            public RectTransform Root { get; }
            public RectTransform Body { get; }

            public Section(
                RectTransform parent, NativeSkin skin, float b, string title, Color titleColor, int rows,
                float detailWidth)
            {
                Root = Widgets.Rect(title, parent);
                Widgets.Column(Root, b * 0.15f);
                Body = Root;

                var header = Widgets.Label("Header", Root, skin, b * 0.8f, titleColor);
                header.text = title;
                Widgets.Fixed(header.rectTransform, b * 1.1f);

                for (var i = 0; i < rows; i++)
                {
                    var row = Widgets.Rect("Row", Root);
                    Widgets.Row(row, b * 0.5f);
                    Widgets.Fixed(row, b * 1.2f);

                    var label = Widgets.Label("Label", row, skin, b * 0.95f, NativeSkin.Text);
                    Widgets.Fixed(label.rectTransform, b * 1.2f).flexibleWidth = 1;

                    var detail = Widgets.Label(
                        "Detail", row, skin, b * 0.8f, NativeSkin.TextDim, TextAlignmentOptions.MidlineRight);
                    detail.richText = true;
                    Widgets.Fixed(detail.rectTransform, b * 1.2f, detailWidth);

                    _rows.Add((label, detail));
                }
            }

            public void Begin() => _used = 0;

            /// <summary>채운 줄을 돌려준다. 커서가 그 줄 위에 있는지 세려면 사각형이 필요하다.</summary>
            public RectTransform Add(string label, string detail, Color tone)
            {
                if (_used >= _rows.Count) return null;

                var row = _rows[_used++];
                row.Label.text = label;
                row.Label.color = tone;
                row.Detail.text = detail;
                Widgets.SetActive(row.Label.transform.parent, true);
                return (RectTransform)row.Label.transform.parent;
            }

            public void End()
            {
                for (var i = _used; i < _rows.Count; i++)
                    Widgets.SetActive(_rows[i].Label.transform.parent, false);

                Widgets.SetActive(Root, _used > 0);
            }
        }

        /// <summary>격자 한 칸. 테두리는 바깥 칠, 속은 안쪽 칠로 낸다.</summary>
        private sealed class Cell
        {
            private readonly float _edge;
            private readonly Image _border;
            private readonly Image _fill;
            private readonly Image _icon;
            private readonly TextMeshProUGUI _name;
            private readonly TextMeshProUGUI _level;
            private readonly string _yieldMark;

            public Cell(RectTransform parent, NativeSkin skin, float b)
            {
                _edge = Mathf.Max(1f, b * 0.1f);
                _yieldMark = skin.YieldMark;

                _border = Widgets.Fill("Cell", parent, NativeSkin.SlotEdge);
                _fill = Widgets.Fill("Fill", _border.rectTransform, NativeSkin.EmptyFill);
                Stretch(_fill.rectTransform, _edge);

                _icon = Widgets.Fill("Icon", _fill.rectTransform, Color.white);
                _icon.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                _icon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                _icon.rectTransform.pivot = new Vector2(0.5f, 1f);
                _icon.rectTransform.sizeDelta = new Vector2(b * 1.3f, b * 1.3f);
                _icon.rectTransform.anchoredPosition = new Vector2(0f, -_edge);
                _icon.preserveAspect = true;

                _name = Widgets.Label(
                    "Name", _fill.rectTransform, skin, b * 0.65f, NativeSkin.Text, TextAlignmentOptions.Top);
                Stretch(_name.rectTransform, _edge);

                _level = Widgets.Label(
                    "Level", _fill.rectTransform, skin, b * 0.8f, NativeSkin.TextBright,
                    TextAlignmentOptions.Bottom);
                Stretch(_level.rectTransform, _edge);
            }

            private static void Stretch(RectTransform rect, float margin)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(margin, margin);
                rect.offsetMax = new Vector2(-margin, -margin);
            }

            /// <summary>커서가 이 칸 위에 있는지 세는 데 쓴다.</summary>
            public RectTransform Rect => _border.rectTransform;

            public void Show() => Widgets.SetActive(_border, true);
            public void Hide() => Widgets.SetActive(_border, false);

            public void SetClosed()
            {
                Paint(NativeSkin.SlotEdge, NativeSkin.ClosedFill, false);
                Clear();
            }

            public void SetEmpty()
            {
                Paint(NativeSkin.SlotEdge, NativeSkin.EmptyFill, false);
                Clear();
            }

            public void SetTablet(string name, int rotation, bool moved, Sprite icon)
            {
                Paint(moved ? NativeSkin.GoldEdge : NativeSkin.TabletEdge, NativeSkin.TabletFill, moved);
                SetIcon(icon);
                _name.text = icon == null ? name : "";
                _name.color = NativeSkin.TabletText;
                _level.text = "회전 " + rotation;
                _level.color = NativeSkin.TabletText;
            }

            public void SetCharm(
                string name, int level, int effective, CharmInactiveReason reason, bool moved,
                Sprite icon, int pinned)
            {
                Paint(moved ? NativeSkin.GoldEdge : NativeSkin.SlotEdge, NativeSkin.SlotFill, moved);
                SetIcon(icon);
                var mark = pinned > 0 ? "★" : pinned < 0 ? _yieldMark : "";
                _name.text = icon == null ? (mark.Length > 0 ? mark + " " + name : name) : "";
                _name.color = NativeSkin.Text;

                // 아이콘이 있으면 이름 줄이 비므로 강화 표시가 레벨 줄로 내려온다. 칸이 좁아
                // 단계는 기호 개수로 적지 않는다 - 몇 단계인지는 쪽지가 말한다.
                var star = icon != null ? mark : "";

                if (reason != CharmInactiveReason.None)
                {
                    _level.text = star + "꺼짐";
                    _level.color = NativeSkin.Bad;
                    return;
                }

                _level.text = star + (effective > 0 ? "+" + effective : effective.ToString());

                // 상한을 넘겨 흘리는 레벨은 값어치가 없다. 색으로만 알린다.
                _level.color = level > effective ? NativeSkin.Orange : NativeSkin.TextBright;
            }

            private void Clear()
            {
                _name.text = "";
                _level.text = "";
                Widgets.SetActive(_icon, false);
            }

            private void SetIcon(Sprite icon)
            {
                _icon.sprite = icon;
                Widgets.SetActive(_icon, icon != null);
            }

            private void Paint(Color border, Color fill, bool thick)
            {
                _border.color = border;
                _fill.color = fill;
                Stretch(_fill.rectTransform, thick ? _edge * 2f : _edge);
            }
        }
    }
}
