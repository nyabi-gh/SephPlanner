using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>창을 열 때의 판 상황. 창은 열려 있는 동안 시간이 멈추므로 한 번 읽으면 된다.</summary>
    internal sealed class BuildContext
    {
        public ICatalog Catalog;
        public GameSnapshot Snapshot;
        public Plan Plan;
        public bool Recommendations;
    }

    /// <summary>
    /// 프리셋 코드, 빌드 우선 콤보와 강화 우선 아티팩트를 고르는 창.
    ///
    /// 셋을 한 창에 둔 것은 셋 다 "무엇을 노릴지"를 정하는 일이고, 한 판에 몇 번 하지 않는
    /// 일이라 창을 따로 열 만큼 자주 쓰지 않기 때문이다. 창을 나누면 단축키도 그만큼 는다.
    ///
    /// <b>목록은 쪽으로 넘긴다.</b> 아티팩트가 서른 개 가까이 될 수 있어 다 펼치면 창이 화면을
    /// 넘어간다. 스크롤 대신 쪽 넘김을 쓰는 것은 설정 창의 화살표와 같은 몸짓이라서다.
    /// </summary>
    internal sealed class BuildWindow : PlannerWindow
    {
        private const int RowsPerPage = 8;

        private enum Tab
        {
            Combos,
            Charms,
        }

        private readonly PluginPreferences _prefs;
        private readonly Func<BuildContext> _source;

        private Tab _tab = Tab.Combos;
        private int _page;
        private string _presetMessage = "";

        private BuildContext _context = new BuildContext();
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<Row> _rows = new List<Row>();

        private TextMeshProUGUI _presetButton;
        private TextMeshProUGUI _presetClear;
        private TextMeshProUGUI _presetStatus;
        private TextMeshProUGUI _comboTab;
        private TextMeshProUGUI _charmTab;
        private TextMeshProUGUI _note;
        private LayoutElement _noteSize;
        private float _noteWidth;
        private TextMeshProUGUI _pageLabel;
        private RectTransform _pager;

        public BuildWindow(PluginPreferences prefs, Func<BuildContext> source)
        {
            _prefs = prefs;
            _source = source;
        }

        protected override string Title => "SephPlanner 빌드";
        protected override float WidthRatio => 34f;
        protected override GameObject DefaultFocus =>
            _presetButton != null ? _presetButton.gameObject : null;

        protected override void BuildBody(RectTransform content)
        {
            BuildPresetSection(content);
            Divider(content);
            BuildTabs(content);

            // 접히는 문단이다. 아티팩트 탭의 안내가 올리는 단계와 내리는 단계를 다 말하면 한 줄을
            // 넘고, 한 줄짜리는 "…" 로 잘린다. 폭은 창 폭에서 테두리와 안쪽 여백을 뺀 것이다.
            _note = Widgets.Paragraph("Note", content, Skin, S(0.75f), NativeSkin.TextDim);
            _noteSize = Widgets.Fixed(_note.rectTransform, S(1.1f));
            _noteWidth = S(WidthRatio) - 2f * S(0.25f) - 2f * S(0.7f);

            var list = Widgets.Rect("List", content);
            Widgets.Column(list, S(0.15f));
            for (var i = 0; i < RowsPerPage; i++)
                _rows.Add(new Row(list, Skin, Base, entry => Toggle(entry, 1), ToggleHold));

            BuildPager(content);
        }

        protected override void Cleared()
        {
            _rows.Clear();
            _entries.Clear();
        }

        private void BuildPresetSection(RectTransform content)
        {
            var row = Widgets.Rect("Preset", content);
            Widgets.Row(row, S(0.6f));
            Widgets.Fixed(row, S(1.4f));

            _presetButton = Widgets.Clickable(
                "Paste", row, Skin, S(0.95f), NativeSkin.TextBright, PastePreset);
            Widgets.Fixed(_presetButton.rectTransform, S(1.4f), S(14f));

            _presetClear = Widgets.Clickable(
                "Clear", row, Skin, S(0.95f), NativeSkin.TextDim, ClearPreset);
            _presetClear.text = "지우기";
            Widgets.Fixed(_presetClear.rectTransform, S(1.4f), S(4f));

            _presetStatus = Widgets.Label("PresetStatus", content, Skin, S(0.75f), NativeSkin.TextDim);
            Widgets.Fixed(_presetStatus.rectTransform, S(1.1f));
        }

        private void BuildTabs(RectTransform content)
        {
            var row = Widgets.Rect("Tabs", content);
            Widgets.Row(row, S(0.6f));
            Widgets.Fixed(row, S(1.4f));

            _comboTab = Tab_(row, "콤보", Tab.Combos);
            _charmTab = Tab_(row, "아티팩트", Tab.Charms);
        }

        private TextMeshProUGUI Tab_(RectTransform row, string label, Tab tab)
        {
            var text = Widgets.Clickable(
                "Tab_" + label, row, Skin, S(1f), NativeSkin.TextDim,
                () =>
                {
                    if (_tab == tab) return;

                    _tab = tab;

                    // 쪽 번호는 목록마다 따로다. 이어받으면 세 쪽짜리에서 넘어온 뒤 빈 쪽이 뜬다.
                    _page = 0;
                    Refresh();
                });
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            Widgets.Fixed(text.rectTransform, S(1.4f), S(7f));
            return text;
        }

        private void BuildPager(RectTransform content)
        {
            _pager = Widgets.Rect("Pager", content);
            Widgets.Row(_pager, S(0.4f));
            Widgets.Fixed(_pager, S(1.4f));

            Step(_pager, "<", -1);

            _pageLabel = Widgets.Label(
                "Page", _pager, Skin, S(0.85f), NativeSkin.TextDim, TextAlignmentOptions.Center);
            Widgets.Fixed(_pageLabel.rectTransform, S(1.4f), S(6f));

            Step(_pager, ">", 1);
        }

        private void Step(RectTransform row, string glyph, int delta)
        {
            var arrow = Widgets.Clickable(
                "Page" + glyph, row, Skin, S(1.1f), NativeSkin.TextBright,
                () =>
                {
                    _page = Mathf.Clamp(_page + delta, 0, LastPage);
                    Refresh();
                });
            arrow.text = glyph;
            arrow.alignment = TextAlignmentOptions.Center;
            Widgets.Fixed(arrow.rectTransform, S(1.4f), S(1.6f));
        }

        private int LastPage => Mathf.Max(0, (_entries.Count - 1) / RowsPerPage);

        public override void Refresh()
        {
            _context = _source() ?? new BuildContext();
            CollectEntries();

            _page = Mathf.Clamp(_page, 0, LastPage);
            RenderPreset();
            RenderTabs();
            RenderRows();
            Widgets.FitHeight(_note, _noteSize, _noteWidth);
        }

        private void RenderPreset()
        {
            var preset = _prefs.Preset();
            _presetButton.text = preset == null ? "빌드 코드 붙여넣기" : "다른 빌드 코드";
            Widgets.SetActive(_presetClear, preset != null);

            if (_presetMessage.Length > 0)
            {
                _presetStatus.text = _presetMessage;
                _presetStatus.color = preset != null ? NativeSkin.TextDim : NativeSkin.Amber;
                return;
            }

            _presetStatus.text = preset != null
                ? PluginPreferences.Summary(preset)
                : "게임에서 프리셋 코드를 복사한 뒤 누르세요.";
            _presetStatus.color = NativeSkin.TextDim;
        }

        private void RenderTabs()
        {
            _comboTab.color = _tab == Tab.Combos ? NativeSkin.Mint : NativeSkin.TextDim;
            _charmTab.color = _tab == Tab.Charms ? NativeSkin.Mint : NativeSkin.TextDim;

            if (_tab == Tab.Combos)
            {
                _note.text = _context.Recommendations
                    ? "누르면 그 콤보를 밀고 있는 빌드로 지정합니다. 후보 추천에서 크게 칩니다."
                    : "후보 추천이 꺼져 있어 지금은 점수에 쓰이지 않습니다. 지정은 남습니다.";
                _note.color = _context.Recommendations ? NativeSkin.TextDim : NativeSkin.Amber;
                return;
            }

            _note.text = PinNote();
            _note.color = NativeSkin.TextDim;
        }

        /// <summary>우클릭은 단계를 내린다. 아티팩트 줄에서만 뜻이 있다.</summary>
        public override void RightClick(Vector2 cursor)
        {
            if (!IsOpen || _tab != Tab.Charms) return;

            foreach (var row in _rows)
            {
                if (row.Entry == null || !Under(row.Rect, cursor)) continue;

                Toggle(row.Entry, -1);
                return;
            }
        }

        private void RenderRows()
        {
            var start = _page * RowsPerPage;
            for (var i = 0; i < _rows.Count; i++)
            {
                var index = start + i;
                if (index < _entries.Count) _rows[i].Show(_entries[index]);
                else _rows[i].Hide();
            }

            _pageLabel.text = $"{_page + 1} / {LastPage + 1}";
            Widgets.SetActive(_pager, _entries.Count > RowsPerPage);

            if (_entries.Count > 0) return;

            // 빈 목록은 아무 말도 하지 않는다. 왜 비었는지는 여기서만 알 수 있다.
            _note.text = _context.Plan == null
                ? "탐험 중이 아닙니다."
                : _tab == Tab.Combos
                    ? "가방에 콤보가 걸린 아티팩트가 없습니다."
                    : "가방에 아티팩트가 없습니다.";
            _note.color = NativeSkin.TextDim;
        }

        private void ToggleHold(Entry entry)
        {
            if (entry == null || entry.EntityId == 0) return;

            _prefs.ToggleHold(entry.EntityId);
            Refresh();
        }

        private void Toggle(Entry entry, int direction)
        {
            if (entry == null) return;

            if (entry.EntityId != 0) _prefs.StepPin(entry.EntityId, direction);
            else if (direction > 0) _prefs.TogglePriority(entry.Key);
            else return;

            Refresh();
        }

        private void CollectEntries()
        {
            _entries.Clear();
            if (_tab == Tab.Combos) CollectCombos();
            else CollectCharms();
        }

        /// <summary>
        /// 지금 세어져 있는 콤보와, 개수가 0이 되어도 지정을 풀 수 있도록 이미 지정된 카테고리를
        /// 함께 보여준다. 순서는 개수 내림차순이되 같으면 식별자로 갈라 - 스냅샷마다 순서가
        /// 흔들리면 같은 자리를 누르려다 다른 것을 누르게 된다.
        /// </summary>
        private void CollectCombos()
        {
            var counts = _context.Snapshot?.Inventory?.ComboCounts;
            var seen = new HashSet<string>();
            var ids = new List<string>();

            if (counts != null)
            {
                foreach (var pair in counts)
                {
                    if (pair.Value > 0 && seen.Add(pair.Key)) ids.Add(pair.Key);
                }
            }
            foreach (var category in _prefs.PriorityCategories)
            {
                if (seen.Add(category)) ids.Add(category);
            }

            ids.Sort((a, b) =>
            {
                var byCount = Count(counts, b).CompareTo(Count(counts, a));
                return byCount != 0 ? byCount : string.CompareOrdinal(a, b);
            });

            foreach (var id in ids)
            {
                var combo = _context.Catalog?.Combo(id);
                if (combo == null) continue;

                var count = Count(counts, id);
                _entries.Add(new Entry
                {
                    Key = id,
                    Name = Naming.Of(combo.Names, combo.Id, id),
                    Detail = Progress(combo, count),
                    Selected = _prefs.IsPriority(id),
                });
            }
        }

        /// <summary>
        /// 아티팩트 탭의 안내. 단계와 배수를 <see cref="PlanPreferences"/>에서 읽어 짓는다 -
        /// 여기 손으로 적어 두면 배수가 바뀌었을 때 이 줄만 옛말을 하게 된다. 실제로 단계가
        /// 셋으로 늘어난 뒤에도 "2배로 칩니다"가 남아 있었다.
        /// </summary>
        private string PinNote()
        {
            var note = new StringBuilder("누르면 ");
            for (var level = 1; level <= PlanPreferences.MaxPinLevel; level++)
            {
                note.Append(Marks(level)).Append(' ')
                    .Append(PlanPreferences.WeightOf(level).ToString("0.##")).Append("배 → ");
            }
            note.Append("해제로 좋은 칸을 먼저 받고, 우클릭은 ");
            for (var level = -1; level >= PlanPreferences.MinPinLevel; level--)
            {
                note.Append(Marks(level)).Append(' ')
                    .Append(PlanPreferences.WeightOf(level).ToString("0.##")).Append("배 → ");
            }
            return note.Append("해제로 남에게 양보합니다. 고정은 배치 조건을 무시하는 칸에만 앉힙니다.").ToString();
        }

        /// <summary>단계를 기호로. 양수는 ★, 음수는 양보 표시를 단계 수만큼.</summary>
        private string Marks(int level) =>
            level > 0 ? new string('★', level) : Repeat(Skin.YieldMark, -level);

        private static string Repeat(string mark, int count)
        {
            var text = new StringBuilder(mark.Length * count);
            for (var i = 0; i < count; i++) text.Append(mark);
            return text.ToString();
        }

        /// <summary>
        /// 줄 앞의 단계 표시. 폭이 흔들리지 않게 네 칸으로 맞춘다 - 누를 때마다 이름이 좌우로
        /// 밀리면 연달아 누르기가 어렵다.
        /// </summary>
        private string PinMark(int level) => level == 0 ? "○   " : Marks(level).PadRight(4);

        private static int Count(Dictionary<string, int> counts, string id) =>
            counts != null && counts.TryGetValue(id, out var value) ? value : 0;

        /// <summary>다음 발동까지 얼마나 남았는지. 게임 콤보 패널과 같은 분수형 표기다.</summary>
        private static string Progress(ComboDefinition combo, int count)
        {
            foreach (var threshold in combo.Thresholds)
            {
                if (count < threshold) return $"{count} / {threshold}";
            }
            return count.ToString();
        }

        /// <summary>
        /// 격자에 놓인 아티팩트. HUD 격자를 누르게
        /// 만들면 무입력 보장이 깨지므로 목록으로 옮겼다. 같은 종류가 여럿이면 한 줄로 묶는다 -
        /// 지정은 종류(엔티티 번호) 단위라 인스턴스를 갈라 봐야 할 것이 없다.
        /// </summary>
        private void CollectCharms()
        {
            var plan = _context.Plan;
            var found = new Dictionary<int, Entry>();
            var order = new List<int>();

            if (plan != null)
            {
                foreach (var pair in plan.Charms)
                {
                    var entityId = pair.Value;
                    if (entityId == 0) continue;

                    plan.Best.EffectiveLevels.TryGetValue(pair.Key, out var level);
                    if (found.TryGetValue(entityId, out var existing))
                    {
                        existing.Count++;
                        existing.Level = Mathf.Max(existing.Level, level);
                        continue;
                    }

                    plan.Names.TryGetValue(pair.Key, out var name);
                    var entry = new Entry
                    {
                        EntityId = entityId,
                        Name = string.IsNullOrEmpty(name) ? "아티팩트" : name,
                        Count = 1,
                        Level = level,
                        Selected = _prefs.IsPinned(entityId),
                        Mark = PinMark(_prefs.PinLevel(entityId)),
                        Held = _prefs.IsHeld(entityId),
                    };
                    found[entityId] = entry;
                    order.Add(entityId);
                }
            }

            // 가방에 없는데 지정돼 있는 것도 보여야 푼다. 다른 판에서 지정한 것이 남아 있는 경우다.
            foreach (var entityId in _prefs.PinnedLevels.Keys.Concat(_prefs.HeldCharms).Distinct().ToList())
            {
                if (found.ContainsKey(entityId)) continue;

                var definition = _context.Catalog?.Charm(entityId);
                found[entityId] = new Entry
                {
                    EntityId = entityId,
                    Name = definition != null
                        ? Naming.Of(definition.Names, definition.Id, "아티팩트")
                        : "아티팩트 #" + entityId,
                    Detail = "가방에 없음",
                    Selected = _prefs.IsPinned(entityId),
                    Mark = PinMark(_prefs.PinLevel(entityId)),
                    Held = _prefs.IsHeld(entityId),
                };
                order.Add(entityId);
            }

            // 지정된 것을 위로 올리지 않는다. 누를 때마다 목록이 재정렬되면 방금 누른 줄이
            // 다른 것으로 바뀌어, 연달아 누르다 엉뚱한 것을 지정하게 된다.
            order.Sort((a, b) =>
            {
                var byName = string.CompareOrdinal(found[a].Name, found[b].Name);
                return byName != 0 ? byName : a.CompareTo(b);
            });

            foreach (var entityId in order)
            {
                var entry = found[entityId];
                if (entry.Detail.Length == 0)
                {
                    entry.Detail = (entry.Level > 0 ? "+" + entry.Level : entry.Level.ToString()) +
                                   (entry.Count > 1 ? "  x" + entry.Count : "");
                }
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// 클립보드에서 프리셋 코드를 읽는다. 게임도 프리셋을 클립보드로 주고받으므로 입력 칸을
        /// 만들 이유가 없다 - 게임에서 복사한 것을 그대로 받으면 된다.
        /// </summary>
        private void PastePreset()
        {
            string code;
            try
            {
                code = GUIUtility.systemCopyBuffer;
            }
            catch (Exception)
            {
                _presetMessage = "클립보드를 읽지 못했습니다. 잠시 뒤 다시 눌러 보세요.";
                Refresh();
                return;
            }

            _prefs.TryImport(code, out _presetMessage);
            Refresh();
        }

        private void ClearPreset()
        {
            _prefs.ClearPreset();
            _presetMessage = "";
            Refresh();
        }

        /// <summary>목록 한 칸의 내용. 콤보면 <see cref="Key"/>가, 아티팩트면 <see cref="EntityId"/>가 찬다.</summary>
        private sealed class Entry
        {
            public string Key = "";
            public int EntityId;
            public string Name = "";
            public string Detail = "";
            public bool Selected;

            /// <summary>
            /// 줄 앞에 붙는 표시. 콤보는 켜고 끄기라 <see cref="Selected"/>의 ●/○ 를 쓰고,
            /// 아티팩트는 단계가 있어 ★ 개수로 대신한다.
            /// </summary>
            public string Mark;
            public int Count;
            public int Level;

            /// <summary>제한 해제 칸에 고정돼 있는가. 아티팩트 줄에만 뜻이 있다.</summary>
            public bool Held;
        }

        /// <summary>목록 한 줄. 줄 전체가 눌린다.</summary>
        private sealed class Row
        {
            private readonly TextMeshProUGUI _name;
            private readonly TextMeshProUGUI _detail;
            private readonly TextMeshProUGUI _hold;
            private readonly Image _background;
            private Entry _entry;

            public Row(
                RectTransform parent, NativeSkin skin, float b, Action<Entry> onClick, Action<Entry> onHold)
            {
                _background = Widgets.ClickableRow(
                    "Entry", parent, NativeSkin.SlotFill, () => onClick(_entry));
                var rect = _background.rectTransform;
                var pad = Mathf.RoundToInt(b * 0.4f);
                Widgets.Row(rect, b * 0.4f).padding = new RectOffset(pad, pad, 0, 0);
                Widgets.Fixed(rect, b * 1.5f);

                _name = Widgets.Label("Name", rect, skin, b * 0.95f, NativeSkin.Text);
                Widgets.Fixed(_name.rectTransform, b * 1.5f).flexibleWidth = 1;

                _detail = Widgets.Label(
                    "Detail", rect, skin, b * 0.8f, NativeSkin.TextDim, TextAlignmentOptions.MidlineRight);
                Widgets.Fixed(_detail.rectTransform, b * 1.5f, b * 7f);

                // 줄 안의 작은 단추. 줄 자체도 눌리지만 레이캐스트는 맨 앞의 것이 받으므로 여기를
                // 누르면 줄의 강화 우선은 움직이지 않는다.
                _hold = Widgets.Clickable("Hold", rect, skin, b * 0.8f, NativeSkin.TextDim, () => onHold(_entry));
                _hold.text = "고정";
                _hold.alignment = TextAlignmentOptions.MidlineRight;
                Widgets.Fixed(_hold.rectTransform, b * 1.5f, b * 2.2f);
            }

            public Entry Entry => _entry;
            public RectTransform Rect => _background.rectTransform;

            public void Show(Entry entry)
            {
                _entry = entry;
                _name.text = (entry.Mark ?? (entry.Selected ? "● " : "○ ")) + entry.Name;
                _name.color = entry.Selected ? NativeSkin.Mint : NativeSkin.Text;
                _detail.text = entry.Detail;
                _hold.color = entry.Held ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_hold, entry.EntityId != 0);
                Widgets.SetActive(_background, true);
            }

            public void Hide()
            {
                _entry = null;
                Widgets.SetActive(_background, false);
            }
        }
    }
}
