using System.Collections.Generic;
using System.Text;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 게임 HUD 캔버스 안에 직접 그리는 화면. 오버레이 창이 하던 표시를 게임 안으로 옮긴 것이다.
    ///
    /// 게임 UI 의 일부라서 전용 전체화면에서도 보이고, 게임 UI 배율을 따르며, 게임이 UI 를 감출 때
    /// 함께 감춰진다 - <c>UIManager.Hide</c>가 <c>UIRoot</c>의 CanvasGroup 알파를 0 으로 만드는데
    /// 우리가 그 밑에 달려 있기 때문이다.
    ///
    /// <b>입력은 가져가지 않는다.</b> <c>UIBase</c>를 상속하지 않아 컨트롤 스택에 들어가지 않으므로
    /// ESC 와 키보드를 뺏지 않고, <see cref="Widgets"/>가 그리는 것마다 <c>raycastTarget</c>을 꺼서
    /// 마우스도 통과시킨다. 그래서 이 화면에는 누르는 것이 없고, 조작은 전부 단축키로 받는다.
    /// </summary>
    internal sealed class NativeHud
    {
        private const int MoveRows = 6;
        private const int OfferRows = 6;
        private const int MixRows = 3;

        private NativeSkin _skin;
        private GameObject _root;

        private TextMeshProUGUI _hint;
        private TextMeshProUGUI _score;
        private TextMeshProUGUI _gain;
        private TextMeshProUGUI _notice;
        private TextMeshProUGUI _nextMove;

        private RectTransform _detail;
        private RectTransform _grid;
        private GridLayoutGroup _gridLayout;
        private readonly List<Cell> _cells = new List<Cell>();

        private Section _moves;
        private Section _offers;
        private Section _mixes;
        private TextMeshProUGUI _offerNotice;
        private TextMeshProUGUI _chips;

        public string Origin { get; private set; } = "";
        public string Blocker { get; private set; } = "";
        public bool IsAlive => _root != null;

        public bool TryCreate(float offsetX, float offsetY, float width)
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
            Build(root, offsetX, offsetY, width);
            Origin = _skin.Origin;
            return true;
        }

        private void Build(UIRoot root, float offsetX, float offsetY, float width)
        {
            var panel = Widgets.Panel("SephPlannerHud", root.transform, _skin, NativeSkin.PanelFill);
            _root = panel.gameObject;

            var rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(offsetX, offsetY);
            rect.sizeDelta = new Vector2(width, 0f);

            Widgets.Column(rect, 3f, new RectOffset(8, 8, 6, 8));
            Widgets.Fitter(rect);

            BuildHeader(rect);
            _notice = Line(rect, 10f, NativeSkin.Amber, 14f);
            _nextMove = Line(rect, 11f, NativeSkin.Text, 16f);

            _detail = Widgets.Rect("Detail", rect);
            Widgets.Column(_detail, 4f);
            Widgets.Fitter(_detail);

            BuildGrid(_detail);
            _moves = new Section(_detail, _skin, "옮길 것", NativeSkin.TextDim, MoveRows);
            BuildOffers(_detail);
            _mixes = new Section(_detail, _skin, "석판 합성기", NativeSkin.Mint, MixRows);
            _chips = Line(_detail, 10f, NativeSkin.TextDim, 14f);
        }

        private void BuildHeader(RectTransform parent)
        {
            var row = Widgets.Rect("Header", parent);
            Widgets.Row(row, 6f);
            Widgets.Fixed(row, 20f);

            var title = Widgets.Label("Title", row, _skin, 13f, NativeSkin.TextBright);
            title.text = "SephPlanner";
            Widgets.Fixed(title.rectTransform, 20f, 92f);

            _score = Widgets.Label("Score", row, _skin, 15f, NativeSkin.TextBright);
            Widgets.Fixed(_score.rectTransform, 20f);

            _gain = Widgets.Label("Gain", row, _skin, 11f, NativeSkin.Good, TextAlignmentOptions.MidlineRight);
            Widgets.Fixed(_gain.rectTransform, 20f, 46f);

            _hint = Widgets.Label("Hint", parent, _skin, 9f, NativeSkin.TextDim);
            Widgets.Fixed(_hint.rectTransform, 12f);
        }

        private void BuildGrid(RectTransform parent)
        {
            _grid = Widgets.Rect("Grid", parent);
            _gridLayout = _grid.gameObject.AddComponent<GridLayoutGroup>();
            _gridLayout.cellSize = new Vector2(44f, 34f);
            _gridLayout.spacing = new Vector2(2f, 2f);
            _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _gridLayout.constraintCount = GridSpec.DefaultWidth;
            Widgets.Fitter(_grid);
        }

        private void BuildOffers(RectTransform parent)
        {
            _offers = new Section(parent, _skin, "지금 집을 수 있는 것", NativeSkin.Mint, OfferRows);

            // 순위를 그대로 믿으면 안 된다는 것은 늘 보여야 한다. 머리글 바로 밑이라야 목록을
            // 읽기 전에 눈에 들어온다.
            _offerNotice = Line(_offers.Body, 9f, NativeSkin.TextDim, 13f);
            _offerNotice.transform.SetSiblingIndex(1);
        }

        private TextMeshProUGUI Line(RectTransform parent, float size, Color color, float height)
        {
            var label = Widgets.Label("Line", parent, _skin, size, color);
            Widgets.Fixed(label.rectTransform, height);
            return label;
        }

        /// <summary>런 밖처럼 보여 줄 배치가 없을 때. 직전 런의 점수를 남겨 두면 거짓말이 된다.</summary>
        public void RenderNotice(string message)
        {
            if (!IsAlive) return;

            _score.text = "SephPlanner";
            _gain.text = "";
            _hint.text = "";
            _nextMove.text = message;
            _notice.text = "";
            Widgets.SetActive(_nextMove, message.Length > 0);
            Widgets.SetActive(_notice, false);
            Widgets.SetActive(_detail, false);
        }

        public void Render(GameSnapshot snapshot, Plan plan, bool expanded, string hint)
        {
            if (!IsAlive) return;

            _hint.text = hint;

            var improved = plan.Gain > 0.001;
            _score.text = $"{plan.Current.Score:0.#} / {plan.Best.Score:0.#}";
            _gain.text = improved ? $"+{plan.Gain:0.#}" : "최적";
            _gain.color = improved ? NativeSkin.Good : NativeSkin.TextDim;

            var warning = Warning(snapshot, plan);
            _notice.text = warning;
            Widgets.SetActive(_notice, warning.Length > 0);

            // 접었을 때는 지금 옮길 것 하나만. 펼치면 아래 목록이 그 일을 하므로 겹치지 않게 접는다.
            var next = plan.Moves.Count > 0 ? plan.Moves[0] : null;
            _nextMove.text = next == null ? "" : $"{next.Label}  {next.Detail}";
            Widgets.SetActive(_nextMove, !expanded && next != null);

            Widgets.SetActive(_detail, expanded);
            if (!expanded) return;

            RenderGrid(snapshot, plan);
            RenderMoves(plan);
            RenderOffers(plan, snapshot.Run?.Gold ?? 0);
            RenderMixes(plan, snapshot.Mixer);
            RenderChips(snapshot);
        }

        /// <summary>
        /// 지금 화면에서 알려야 할 것. 오버레이와 같은 순서다 - 점수를 믿을 수 없는 상황이
        /// 멀티 안내보다 먼저다.
        /// </summary>
        private static string Warning(GameSnapshot snapshot, Plan plan)
        {
            if (plan.Best.UnplacedTablets > 0)
                return $"석판 {plan.Best.UnplacedTablets}개는 놓을 자리가 없어 계산에서 빠졌습니다.";

            if (plan.LevelMismatches > 0)
                return $"칸 {plan.LevelMismatches}개의 레벨이 게임과 다릅니다. 점수가 실제와 다를 수 있습니다.";

            if (plan.SkippedOffers > 0)
                return $"선택지가 많아 {plan.SkippedOffers}개는 평가하지 못했습니다.";

            return snapshot.IsMultiplayer ? "멀티플레이 세션 - 제안만 표시합니다." : "";
        }

        private void RenderGrid(GameSnapshot snapshot, Plan plan)
        {
            var inventory = snapshot.Inventory;
            var total = inventory.Width * inventory.Height;
            _gridLayout.constraintCount = inventory.Width;

            while (_cells.Count < total) _cells.Add(new Cell(_grid, _skin));
            for (var i = total; i < _cells.Count; i++) _cells[i].Hide();

            var tabletCells = new Dictionary<GridPos, TabletPlacement>();
            foreach (var placement in plan.Best.Tablets) tabletCells[placement.Position] = placement;

            var moved = new HashSet<GridPos>();
            foreach (var move in plan.Moves) moved.Add(move.To);

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
                    cell.SetTablet(
                        Naming.OfTablet(tablet), tablet.Rotation, moved.Contains(position),
                        IconOf(tablet.Definition.EntityId));
                }
                else if (plan.Best.Levels.TryGetValue(position, out var level))
                {
                    plan.Best.EffectiveLevels.TryGetValue(position, out var effective);
                    plan.Best.InactiveCells.TryGetValue(position, out var reason);
                    plan.Names.TryGetValue(position, out var name);
                    plan.Charms.TryGetValue(position, out var charmId);
                    cell.SetCharm(
                        name ?? "", level, effective, reason, moved.Contains(position), IconOf(charmId));
                }
                else
                {
                    cell.SetEmpty();
                }
            }
        }

        /// <summary>
        /// 게임이 들고 있는 스프라이트를 그대로 쓴다. 오버레이는 PNG 로 떠 둔 것을 읽어야 했지만
        /// 게임 안에서는 원본이 이미 메모리에 있다.
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

        private void RenderOffers(Plan plan, int gold)
        {
            _offers.Begin();

            var guessed = 0;
            for (var i = 0; i < plan.Offers.Count && i < OfferRows; i++)
            {
                var advice = plan.Offers[i];
                var charm = advice.Candidate.Charm;
                if (charm != null &&
                    CharmWorth.Resolve(charm).Source == CharmWorthSource.Rarity) guessed++;

                _offers.Add(advice.Candidate.Name, Detail(advice), NameTone(advice));
            }
            _offers.End();

            _offerNotice.text = guessed > 0
                ? $"순위는 참고용입니다 — 이 중 {guessed}개는 값어치를 레어도로 어림잡았습니다."
                : "순위는 참고용입니다.";
            Widgets.SetActive(_offerNotice, plan.Offers.Count > 0);
        }

        private static Color NameTone(OfferAdvice advice) =>
            !advice.Affordable ? NativeSkin.TextDim
            : advice.MatchesPriority || advice.MatchesPreset ? NativeSkin.Mint
            : NativeSkin.Text;

        /// <summary>
        /// 한 줄 오른쪽에 붙는 것들. 색이 서로 다르므로 TMP 서식으로 칠한다 - 텍스트를 여럿으로
        /// 쪼개 배치하는 것보다 짧고, 폭이 바뀌어도 알아서 붙는다.
        /// </summary>
        private static string Detail(OfferAdvice advice)
        {
            var parts = new StringBuilder();

            if (advice.ComboText.Length > 0)
            {
                parts.Append(Tint(advice.ComboText, advice.ComboCompletes ? NativeSkin.Good : NativeSkin.Mint))
                     .Append("  ");
            }

            var reach = Reach(advice.Effect);
            if (reach.Length > 0) parts.Append(Tint(reach, NativeSkin.TextDim)).Append("  ");

            if (advice.Candidate.Price > 0)
            {
                parts.Append(Tint(
                    advice.Candidate.Price + "골드",
                    advice.Affordable ? NativeSkin.TextDim : NativeSkin.Bad)).Append("  ");
            }

            var gain = advice.Gain;
            var text = gain > 0.001 ? $"+{gain:0.#}" : gain < -0.001 ? $"{gain:0.#}" : "0";
            parts.Append(Tint(text, gain > 0.001 ? NativeSkin.Good : gain < -0.001 ? NativeSkin.Bad : NativeSkin.TextDim));
            return parts.ToString();
        }

        private static string Reach(TabletEffectSummary effect)
        {
            if (effect.IsEmpty) return "";

            var text = effect.RaisedCells > 0 ? $"{effect.RaisedCells}칸 +{effect.RaisedTotal}" : "";
            if (effect.LoweredCells > 0) text += $" −{effect.LoweredTotal}";
            if (effect.DisabledCells > 0) text += $" 막힘{effect.DisabledCells}";
            return text.Trim();
        }

        private void RenderMixes(Plan plan, MixerState mixer)
        {
            _mixes.Begin();
            for (var i = 0; i < plan.Mixes.Count && i < MixRows; i++)
            {
                var advice = plan.Mixes[i];
                _mixes.Add(
                    advice.NameA + " + " + advice.NameB,
                    Turn(advice) + Tint($"+{advice.Gain:0.#}",
                        advice.Gain > 0.001 ? NativeSkin.Good : NativeSkin.TextDim),
                    advice.Affordable ? NativeSkin.Text : NativeSkin.TextDim);
            }
            _mixes.End();

            // 합성기가 이 층에 없거나 이미 썼으면 절 자체를 접는다.
            Widgets.SetActive(_mixes.Root, mixer != null && !mixer.Used && plan.Mixes.Count > 0);
        }

        /// <summary>
        /// 합성기에 넣기 전에 맞춰 두어야 하는 회전. 사람이 손으로 돌려야 하는 일이라 빠뜨리면
        /// 답이 반쪽이 된다.
        /// </summary>
        private static string Turn(MixAdvice advice)
        {
            if (advice.RotationA == 0 && advice.RotationB == 0) return "";

            return Tint($"회전 {advice.RotationA}/{advice.RotationB}", NativeSkin.Amber) + "  ";
        }

        private void RenderChips(GameSnapshot snapshot)
        {
            var counts = snapshot.Inventory?.ComboCounts;
            if (counts == null || counts.Count == 0)
            {
                Widgets.SetActive(_chips, false);
                return;
            }

            var text = new StringBuilder();
            foreach (var pair in counts)
            {
                if (pair.Value <= 0) continue;
                if (text.Length > 0) text.Append(" · ");
                text.Append(pair.Key).Append(' ').Append(pair.Value);
            }

            _chips.text = text.ToString();
            Widgets.SetActive(_chips, text.Length > 0);
        }

        public void SetVisible(bool visible)
        {
            if (IsAlive && _root.activeSelf != visible) _root.SetActive(visible);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);

            _root = null;
            _cells.Clear();
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

            public Section(RectTransform parent, NativeSkin skin, string title, Color titleColor, int rows)
            {
                Root = Widgets.Rect(title, parent);
                Widgets.Column(Root, 2f);
                Widgets.Fitter(Root);
                Body = Root;

                var header = Widgets.Label("Header", Root, skin, 10f, titleColor);
                header.text = title;
                Widgets.Fixed(header.rectTransform, 13f);

                for (var i = 0; i < rows; i++)
                {
                    var row = Widgets.Rect("Row", Root);
                    Widgets.Row(row, 6f);
                    Widgets.Fixed(row, 14f);

                    var label = Widgets.Label("Label", row, skin, 11f, NativeSkin.Text);
                    var element = Widgets.Fixed(label.rectTransform, 14f);
                    element.flexibleWidth = 1;

                    var detail = Widgets.Label(
                        "Detail", row, skin, 10f, NativeSkin.TextDim, TextAlignmentOptions.MidlineRight);
                    detail.richText = true;
                    Widgets.Fixed(detail.rectTransform, 14f, 150f);

                    _rows.Add((label, detail));
                }
            }

            public void Begin() => _used = 0;

            public void Add(string label, string detail, Color tone)
            {
                if (_used >= _rows.Count) return;

                var row = _rows[_used++];
                row.Label.text = label;
                row.Label.color = tone;
                row.Detail.text = detail;
                Widgets.SetActive(row.Label.transform.parent, true);
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
            private readonly Image _border;
            private readonly Image _fill;
            private readonly Image _icon;
            private readonly TextMeshProUGUI _name;
            private readonly TextMeshProUGUI _level;

            public Cell(RectTransform parent, NativeSkin skin)
            {
                _border = Widgets.Fill("Cell", parent, NativeSkin.SlotEdge);

                _fill = Widgets.Fill("Fill", _border.rectTransform, NativeSkin.EmptyFill);
                Stretch(_fill.rectTransform, 1f);

                _icon = Widgets.Fill("Icon", _fill.rectTransform, Color.white);
                _icon.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                _icon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                _icon.rectTransform.pivot = new Vector2(0.5f, 1f);
                _icon.rectTransform.sizeDelta = new Vector2(16f, 16f);
                _icon.rectTransform.anchoredPosition = new Vector2(0f, -1f);
                _icon.preserveAspect = true;

                _name = Widgets.Label("Name", _fill.rectTransform, skin, 8f, NativeSkin.Text,
                    TextAlignmentOptions.Top);
                Stretch(_name.rectTransform, 1f);

                _level = Widgets.Label("Level", _fill.rectTransform, skin, 10f, NativeSkin.TextBright,
                    TextAlignmentOptions.Bottom);
                Stretch(_level.rectTransform, 1f);
            }

            private static void Stretch(RectTransform rect, float margin)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(margin, margin);
                rect.offsetMax = new Vector2(-margin, -margin);
            }

            public void Show() => Widgets.SetActive(_border, true);
            public void Hide() => Widgets.SetActive(_border, false);

            public void SetClosed()
            {
                Paint(NativeSkin.SlotEdge, NativeSkin.ClosedFill, false);
                _name.text = "";
                _level.text = "";
                Widgets.SetActive(_icon, false);
            }

            public void SetEmpty()
            {
                Paint(NativeSkin.SlotEdge, NativeSkin.EmptyFill, false);
                _name.text = "";
                _level.text = "";
                Widgets.SetActive(_icon, false);
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
                string name, int level, int effective, CharmInactiveReason reason, bool moved, Sprite icon)
            {
                Paint(moved ? NativeSkin.GoldEdge : NativeSkin.SlotEdge, NativeSkin.SlotFill, moved);
                SetIcon(icon);
                _name.text = icon == null ? name : "";
                _name.color = NativeSkin.Text;

                if (reason != CharmInactiveReason.None)
                {
                    _level.text = "꺼짐";
                    _level.color = NativeSkin.Bad;
                    return;
                }

                _level.text = effective > 0 ? "+" + effective : effective.ToString();

                // 상한을 넘겨 흘리는 레벨은 값어치가 없다. 색으로만 알린다.
                _level.color = level > effective ? NativeSkin.Orange : NativeSkin.TextBright;
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
                Stretch(_fill.rectTransform, thick ? 2f : 1f);
            }
        }
    }
}
