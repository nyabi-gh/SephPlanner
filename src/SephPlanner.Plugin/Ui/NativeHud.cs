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
        private const int EnchantRows = 3;

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
        private LayoutElement _scoreSize;
        private TextMeshProUGUI _gain;
        private LayoutElement _gainSize;
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
        private Section _enchants;
        private Section _discards;
        private TextMeshProUGUI _offerNotice;
        private TextMeshProUGUI _chips;

        private readonly Tooltip _tooltip = new Tooltip();
        private readonly List<HoverTarget> _hover = new List<HoverTarget>();
        private bool _hoverable;

        /// <summary>마지막으로 그린 근거와, 그린 것이 있는지.</summary>
        private HudFrameKey _drawn;
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
            _enchants = new Section(_detail, _skin, _base, "인챈트", NativeSkin.Mint, EnchantRows, detail);
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
            _score = Widgets.Paragraph("Score", parent, _skin, S(1f), NativeSkin.TextBright);
            _scoreSize = Widgets.Fixed(_score.rectTransform, S(1.5f));
            _gain = Widgets.Paragraph("Gain", parent, _skin, S(0.85f), NativeSkin.Good);
            _gainSize = Widgets.Fixed(_gain.rectTransform, S(1.2f));

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
            var key = new HudFrameKey(frame);
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
                ? $"점수: 현재 {plan.Current.Score:0.#} → 획득 후 {score:0.#}"
                : $"점수: 현재 {plan.Current.Score:0.#} → 제안 {score:0.#}";
            Hover(_score.rectTransform, "배치 평가 점수", () =>
                "게임 효과를 환산한 추정 점수이며 실제 피해량이 아닙니다. 사용 유지·침·모래시계·별조각 우선·콤보 지정 등은 점수보다 먼저 적용됩니다.");
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
                plan.Best.SupportTargetMatches > plan.Current.SupportTargetMatches)
            {
                _gain.text = $"침·모래시계·별조각 우선 ({gain:+0.#;-0.#;0})";
                _gain.color = NativeSkin.Mint;
            }

            if (previewed == null && plan.HasPlacementChanges &&
                plan.Best.UnretainedCharms.Count < plan.Current.UnretainedCharms.Count)
            {
                _gain.text = $"사용 유지 우선 ({gain:+0.#;-0.#;0})";
                _gain.color = NativeSkin.Mint;
            }

            Widgets.FitHeight(_score, _scoreSize, inner);
            Widgets.FitHeight(_gain, _gainSize, inner);
            var warning = Warning(
                snapshot, plan, frame.MultiplayerAutoPlace, frame.QueryVerified,
                frame.RuntimeVerification, frame.RuntimeVerificationReason, frame.Stale);
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
            RenderMixes(plan, snapshot.Mixer, frame.MixerOpen, frame.AdviceBusy);
            RenderEnchants(plan, snapshot.EnchantChance, frame.EnchantOpen, frame.AdviceBusy);
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
            PlanVerificationStatus runtimeVerification, string runtimeVerificationReason, bool stale)
        {
            var warnings = new List<string>();

            // 아래 경고들이 전부 이 계획을 근거로 하므로 낡았다는 것이 먼저 와야 한다.
            if (stale)
                warnings.Add("갱신 중 - 가방이 바뀌어 다시 계산하고 있습니다. 아래는 직전 계획입니다.");

            if (!queryVerified)
                warnings.Add("석판 효과 계산을 검증하지 못해 자동 배치를 사용할 수 없습니다. ‘아이템 데이터 다시 읽기’(기본 F9)를 실행하세요.");

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
                warnings.Add($"배치 조건을 무시하는 칸이 모자라 ‘조건 무시 칸’ 지정 {plan.Best.UnheldCharms.Count}개를 지키지 못했습니다.");

            warnings.AddRange(plan.ComboPlacementWarnings);
            warnings.AddRange(plan.RetentionWarnings);
            warnings.AddRange(plan.SupportWarnings);
            warnings.AddRange(plan.ActivationWarnings);

            if (snapshot.IsMultiplayer)
            {
                warnings.Add(multiplayerAutoPlace
                    ? "멀티플레이 세션 - 자동 배치 허용됨. 같이 하는 사람의 동의를 받으세요."
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

                    var pinned = frame.Prefs != null ? frame.Prefs.PinLevel(charmId) : 0;
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
                var lines = Explain.Cell(name, level, effective, reason, definition, values);

                // 첫 줄은 쪽지의 제목으로 올라간다.
                lines.RemoveAt(0);
                if (definition?.HasNoActivationEffect == true)
                    lines.Add("자체 활성 효과가 없어 감점 칸을 활용할 수 있습니다. 사용 유지·조건 무시 칸 지정과 주변 효과는 계속 고려합니다.");
                if (pinned > 0)
                {
                    lines.Add(
                        $"강화칸 우선 {new string('★', pinned)} - 배치 평가에서 이득을 " +
                        $"{PlanPreferences.WeightOf(pinned):0.##}배로 칩니다. 게임 효과의 배수가 아니며 패널티는 그대로 반영합니다." +
                        (reason == CharmInactiveReason.Weapon
                            ? " 지금은 꺼져 있어 점수에는 들어가지 않지만, 자리는 이 지정대로 잡습니다."
                            : ""));
                }
                else if (pinned < 0)
                {
                    lines.Add(
                        $"강화칸 양보 {string.Concat(System.Linq.Enumerable.Repeat(_skin.YieldMark, -pinned))} - 배치 평가에서 이득을 " +
                        $"{PlanPreferences.WeightOf(pinned):0.##}배로 칩니다. 패널티는 그대로 반영합니다. 효과를 끄거나 침 연결을 금지하지는 않습니다.");
                }
                if (frame.Prefs != null && frame.Prefs.IsRetained(charmId))
                    lines.Add("사용 유지: 활성 상태와 지원 연결을 지키고 빼기·교체 추천에서 보호합니다.");
                if (frame.Prefs != null && frame.Prefs.IsHeld(charmId))
                    lines.Add("조건 무시 칸: 배치 조건을 무시하는 칸을 우선합니다. 좌표나 회전을 고정하지는 않습니다.");
                if (frame.Prefs != null && frame.Prefs.IsSupportTarget(charmId))
                    lines.Add("침·모래시계·별조각 우선: 이 아이템을 우선 강화합니다. 콤보 지정과 강화칸 우선·양보보다 먼저 적용됩니다.");
                if (frame.Prefs != null && frame.Prefs.IsDeactivationAllowed(charmId))
                    lines.Add("끄기 허용: 배치 이득이 있으면 효과를 꺼도 됩니다. 항상 끄지는 않으며 사용 유지가 함께 켜져 있으면 효과를 유지합니다.");
                if (frame.Prefs?.LevelCap(charmId) is int cap)
                    lines.Add(cap == 0
                        ? "목표 레벨: 0레벨로 평가해 효과만 켜 두고 레벨 이득은 세지 않습니다. 실제 배치 레벨은 더 높을 수 있습니다."
                        : $"목표 레벨: {cap}레벨까지만 이득으로 평가합니다. 실제 배치 레벨은 더 높을 수 있습니다.");
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

                // 손으로 채운 가치를 함께 넘긴다. 빠뜨리면 채워 넣은 아티팩트까지 "레어도로
                // 어림잡았다"고 세어, 화면이 실제보다 못 미더운 말을 하게 된다.
                if (charm != null)
                {
                    var source = CharmWorth.Resolve(charm, frame.Values.Of(charm)).Source;
                    if (source == CharmWorthSource.Rarity || source == CharmWorthSource.MeasuredFloor)
                        guessed++;
                }

                var picked = previewed != null && previewed.Key == advice.Key;
                var row = _offers.Add(
                    (picked ? "> " : "") + advice.Candidate.Name, Detail(advice),
                    picked ? NativeSkin.GoldEdge : NameTone(advice));
                var gold = frame.Gold;
                var values = frame.Values;
                Hover(row, advice.Candidate.Name, () => Explain.Join(Explain.Offer(advice, gold, values)));
            }
            _offers.End();

            // 후보가 있는데 아직 조언이 안 붙었으면 그렇다고 말한다. 배치가 먼저 게시되므로
            // 창을 연 직후의 빈 칸은 "볼 것이 없다" 가 아니다.
            var pending = plan.Offers.Count == 0 && frame.AdviceBusy &&
                          frame.Snapshot != null && frame.Snapshot.Offers.Count > 0;
            _offerNotice.text = pending
                ? "후보를 계산하고 있습니다."
                : guessed > 0
                    ? $"순위는 참고용입니다 — 이 중 {guessed}개는 값어치를 레어도로 어림잡았습니다."
                    : "순위는 참고용입니다.";
            Widgets.SetActive(_offerNotice, plan.Offers.Count > 0 || pending);
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

            if (!advice.Available) return Tint("배치 미확보", NativeSkin.Bad);

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

            // 순위는 다음 칸이 열린 판의 값으로 세우므로, 그것이 지금 값과 다르면 적어 준다.
            // 적지 않으면 증가분이 낮은 것이 위에 있는 이유가 화면에서 사라진다.
            var soonTag = Explain.SoonTag(advice.Gain, advice.SoonGain);
            if (soonTag.Length > 0) parts.Append(Tint(soonTag, NativeSkin.Mint)).Append("  ");

            var gain = advice.Gain;
            var text = gain > 0.001 ? $"+{gain:0.#}" : gain < -0.001 ? $"{gain:0.#}" : "0";
            parts.Append(Tint(
                text, gain > 0.001 ? NativeSkin.Good : gain < -0.001 ? NativeSkin.Bad : NativeSkin.TextDim));
            return parts.ToString();
        }

        private void RenderDiscards(Plan plan, bool visible)
        {
            _discards.Begin();
            foreach (var advice in plan.Discards)
            {
                var row = _discards.Add(advice.Name, $"제외 후 재배치 +{advice.Gain:0.#}", NativeSkin.Text);
                Hover(row, advice.Name, () =>
                    $"현재 위치: {advice.Position.X + 1}열 {advice.Position.Y + 1}행\n" +
                    "이 항목 하나를 가방에서 빼고 다시 배치했을 때의 추정 이득입니다. F8은 아이템을 제거하지 않습니다.\n" +
                    "콤보 단계가 유지되는 후보만 표시합니다. 실제 전투 효과와 다를 수 있습니다." +
                    (advice.SoonGain.HasValue
                        ? $"\n가방이 한 칸 더 열려도 여전히 이득입니다({advice.SoonGain.Value:+0.#;-0.#;0}). "
                          + "그렇지 않은 것은 목록에서 뺐습니다."
                        : "") +
                    (advice.ReducesComboCount ? "\n콤보 개수는 줄어 다음 단계가 멀어질 수 있습니다." : "") +
                    (advice.Activated.Count > 0 ? "\n켜지는 아티팩트: " + string.Join(", ", advice.Activated) : ""));
            }
            _discards.End();
            Widgets.SetActive(_discards.Root, visible && plan.Discards.Count > 0);
        }

        /// <summary>
        /// 어느 아티팩트에 인챈트를 걸지. 제단과 인챈트 물약이 여는 창이 떠 있을 때만 보인다 -
        /// 합성 추천이 상점을 열어도 따라 붙던 것과 같은 실수를 반복하지 않는다.
        /// </summary>
        private void RenderEnchants(
            Plan plan, EnchantChanceState chance, bool enchantOpen, bool adviceBusy)
        {
            _enchants.Begin();
            for (var i = 0; i < plan.Enchants.Count && i < EnchantRows; i++)
            {
                var advice = plan.Enchants[i];
                var soonTag = Explain.SoonTag(advice.Gain, advice.SoonGain);
                var row = _enchants.Add(
                    advice.Name,
                    Tint($"{advice.Enchant}/{advice.MaxEnchant}", NativeSkin.TextDim) + "  " +
                    (soonTag.Length > 0 ? Tint(soonTag, NativeSkin.Mint) + "  " : "") +
                    Tint($"{advice.Gain:+0.#;-0.#;0}",
                        advice.Gain > 0.001 ? NativeSkin.Good : NativeSkin.TextDim),
                    NativeSkin.Text);
                Hover(row, advice.Name,
                    () => $"현재 위치: {advice.Position.X + 1}열 {advice.Position.Y + 1}행\n"
                          + Explain.Join(Explain.Enchant(advice)));
            }
            if (enchantOpen && plan.Enchants.Count == 0)
            {
                // 조언은 배치보다 뒤에 붙는다. 그 사이에 "없음" 이라고 하면 거짓말이 된다.
                if (adviceBusy)
                    _enchants.Add("계산 중", "인챈트 추천을 계산하고 있습니다.", NativeSkin.TextDim);
                else
                    _enchants.Add("추천 대상 없음", chance == null ? "제단 정보를 읽는 중입니다." :
                        !chance.Available ? "제단에서 떨어져 있어 아직 계산하지 않았습니다. 가까이 가면 계산합니다." :
                        "인챈트로 값이 오르는 아티팩트를 찾지 못했습니다. 인챈트가 이미 상한에 "
                        + "닿았거나, 올려도 효과에 반영되지 않는 자리입니다.", NativeSkin.TextDim);
            }
            _enchants.End();

            Widgets.SetActive(_enchants.Root, enchantOpen);
        }

        private void RenderMixes(Plan plan, MixerState mixer, bool mixerOpen, bool adviceBusy)
        {
            _mixes.Begin();
            for (var i = 0; i < plan.Mixes.Count && i < MixRows; i++)
            {
                var advice = plan.Mixes[i];
                var name = advice.NameA + " + " + advice.NameB;
                var soonTag = Explain.SoonTag(advice.Gain, advice.SoonGain);
                var row = _mixes.Add(
                    name,
                    RotationTag(advice) +
                    (soonTag.Length > 0 ? Tint(soonTag, NativeSkin.Mint) + "  " : "") +
                    Tint($"{advice.Gain:+0.#;-0.#;0}",
                        advice.Gain > 0.001 ? NativeSkin.Good : NativeSkin.TextDim),
                    advice.Affordable ? NativeSkin.Text : NativeSkin.TextDim);
                Hover(row, name, () => Explain.Join(Explain.Mix(advice)));
            }
            if (mixerOpen && plan.Mixes.Count == 0)
            {
                // 조언은 배치보다 뒤에 붙는다. 그 사이에 "없음" 이라고 하면 거짓말이 된다.
                if (adviceBusy)
                    _mixes.Add("계산 중", "합성 추천을 계산하고 있습니다.", NativeSkin.TextDim);
                else
                    _mixes.Add("추천 조합 없음", mixer == null ? "합성기 정보를 읽는 중입니다." :
                        mixer.Used ? "이 합성기는 이미 사용했습니다." :
                        mixer.Near == false ? "합성기에서 떨어져 있어 아직 계산하지 않았습니다. 가까이 가면 계산합니다." :
                        "현재 석판에서 추천할 수 있는 조합을 찾지 못했습니다.", NativeSkin.TextDim);
            }
            _mixes.End();

            // 합성 추천은 합성기 창을 열었을 때만 보인다.
            //
            // 예전에는 층에 합성기가 있기만 하면 계속 띄웠다 - "합성기 앞에 서기 전에 무엇을
            // 합칠지 정해 두는 편이 쓸모 있다" 는 생각이었다. 실제로는 <b>상점을 열어도 합성
            // 추천이 따라 붙었다</b>. 조언 칸은 후보가 생기면 저절로 펼쳐지는데, 펼쳐진 김에
            // 합성 줄까지 같이 보였기 때문이다. 상점에서 살 것을 고르는 중에 합성 이야기가
            // 끼어드는 것은 도움이 아니라 잡음이라는 제보를 받고 창 기준으로 좁혔다.
            Widgets.SetActive(_mixes.Root, mixerOpen);
        }

        /// <summary>
        /// 합성 창에서 손으로 돌려야 하는 횟수. 빠뜨리면 답이 반쪽이 된다. 어느 석판을 돌리는지는
        /// <see cref="Explain.Turn"/>가 쪽지에서 말한다.
        /// </summary>
        private static string RotationTag(MixAdvice advice)
        {
            var tag = Explain.TurnTag(advice);
            return tag.Length == 0 ? "" : Tint(tag, NativeSkin.Amber) + "  ";
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
                _level.text = rotation * 90 + "°";
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

                _level.text = star + Explain.Level(effective);

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
