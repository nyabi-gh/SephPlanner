using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
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
        private const int RowsPerPage = 6;

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
        protected override float WidthRatio => 38f;
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
                _rows.Add(new Row(
                    list, Skin, Base, entry => Toggle(entry, 1), ToggleHold, ToggleRetain, ToggleDeactivation,
                    ToggleSupportTarget, StepCap));

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
            _presetClear.text = "코드 해제";
            Widgets.Fixed(_presetClear.rectTransform, S(1.4f), S(5f));

            var reset = Widgets.Clickable(
                "ResetBuild", row, Skin, S(0.95f), NativeSkin.TextDim, ResetBuild);
            reset.text = "빌드 지정 초기화";
            Widgets.Fixed(reset.rectTransform, S(1.4f), S(9f));

            _presetStatus = Widgets.Label("PresetStatus", content, Skin, S(0.75f), NativeSkin.TextDim);
            Widgets.Fixed(_presetStatus.rectTransform, S(1.1f));
        }

        private void BuildTabs(RectTransform content)
        {
            var row = Widgets.Rect("Tabs", content);
            Widgets.Row(row, S(0.6f));
            Widgets.Fixed(row, S(1.4f));

            _comboTab = Tab_(row, "콤보 우선", Tab.Combos);
            _charmTab = Tab_(row, "아티팩트 설정", Tab.Charms);
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
            _presetButton.text = preset == null ? "빌드 코드 붙여넣기" : "빌드 코드 바꾸기";
            Widgets.SetActive(_presetClear, preset != null);

            if (_prefs.StorageMessage.Length > 0)
            {
                _presetStatus.text = _prefs.StorageMessage;
                _presetStatus.color = NativeSkin.Amber;
                return;
            }

            if (_presetMessage.Length > 0)
            {
                _presetStatus.text = _presetMessage;
                _presetStatus.color = preset != null ? NativeSkin.TextDim : NativeSkin.Amber;
                return;
            }

            _presetStatus.text = preset != null
                ? PluginPreferences.Summary(preset)
                : "게임에서 빌드 코드를 복사한 뒤 누르세요.";
            _presetStatus.color = NativeSkin.TextDim;
        }

        private void RenderTabs()
        {
            _comboTab.color = _tab == Tab.Combos ? NativeSkin.Mint : NativeSkin.TextDim;
            _charmTab.color = _tab == Tab.Charms ? NativeSkin.Mint : NativeSkin.TextDim;

            if (_tab == Tab.Combos)
            {
                _note.text = "누르면 콤보 우선을 켜거나 끕니다. ●는 선택한 콤보입니다.\n"
                    + "열쇠·종이·침은 선택한 콤보를 만드는 자리를 강화칸 우선·양보보다 먼저 고릅니다. "
                    + "침은 연결한 대상의 콤보를 복사하므로, 양보한 아티팩트에도 붙을 수 있습니다.\n"
                    + "특정 아이템에 침을 붙이려면 아티팩트 설정에서 그 아이템의 ‘침·모래시계·별조각 우선’을 켜세요. 콤보 지정보다 먼저 적용됩니다."
                    + (_context.Recommendations ? "" : "\n획득·합성·인챈트·제거 추천은 꺼져 있지만 배치 지정은 적용됩니다.");
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

                // 제한 버튼 위에서는 그 제한을 내린다. 줄 전체의 우클릭은 지금처럼 ★ 를 내린다.
                if (Under(row.CapRect, cursor)) StepCap(row.Entry, -1);
                else Toggle(row.Entry, -1);
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

        private void ToggleRetain(Entry entry)
        {
            if (entry == null || entry.EntityId == 0) return;
            _prefs.ToggleRetain(entry.EntityId);
            Refresh();
        }

        private void ToggleDeactivation(Entry entry)
        {
            if (entry == null || entry.EntityId == 0) return;
            _prefs.ToggleDeactivation(entry.EntityId);
            Refresh();
        }

        /// <summary>
        /// 레벨 제한을 한 단계 옮긴다. 상한은 카탈로그의 값이라 아티팩트마다 다르고, 상한과 같은
        /// 제한은 제한이 아니므로 그 직전까지만 돈다.
        /// </summary>
        private void StepCap(Entry entry, int direction)
        {
            if (entry == null || entry.EntityId == 0) return;

            var definition = _context.Catalog?.Charm(entry.EntityId);
            _prefs.StepLevelCap(entry.EntityId, direction, definition?.MaxLevel ?? 5);
            Refresh();
        }

        private void ToggleSupportTarget(Entry entry)
        {
            if (entry == null || entry.EntityId == 0) return;

            _prefs.ToggleSupportTarget(entry.EntityId);
            Refresh();
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
            foreach (var category in _prefs.EffectivePriorityCategories())
            {
                if (seen.Add(category)) ids.Add(category);
            }

            var items = _context.Snapshot?.Inventory?.Items;
            if (items != null)
            {
                foreach (var item in items)
                {
                    var definition = _context.Catalog?.Charm(item.DefinitionId);
                    if (definition == null) continue;
                    foreach (var category in definition.LineCategories)
                        if (seen.Add(category)) ids.Add(category);
                }
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
            var note = new StringBuilder("아이템 이름 좌클릭: 강화칸 우선 ");
            for (var level = 1; level <= PlanPreferences.MaxPinLevel; level++)
            {
                note.Append(Marks(level)).Append(' ')
                    .Append(PlanPreferences.WeightOf(level).ToString("0.##")).Append("배 → ");
            }
            note.Append("해제. 우클릭: 강화칸 양보 ");
            for (var level = -1; level >= PlanPreferences.MinPinLevel; level--)
            {
                note.Append(Marks(level)).Append(' ')
                    .Append(PlanPreferences.WeightOf(level).ToString("0.##")).Append("배 → ");
            }
            return note.Append("해제. 반대 방향은 한 단계씩 되돌립니다. 배수는 게임 효과가 아닌 배치 평가에 적용됩니다.\n"
                + "민트색 버튼은 켜진 설정입니다. 설정은 같은 종류의 아이템 모두에 적용됩니다.\n"
                + "사용 유지: 효과와 지원 연결을 지키고 제거·교체 추천에서 보호합니다. 끄기 허용: 이득이 있으면 효과를 꺼도 됩니다. 둘 다 켜면 사용 유지가 우선합니다.\n"
                + "침·모래시계·별조각 우선: 이 아이템을 우선 강화합니다. 콤보 지정과 강화칸 우선·양보보다 먼저 적용하며, 연결을 찾지 못하면 이유를 알립니다.\n"
                + "목표 레벨: 그 레벨까지만 이득으로 평가합니다. 실제 레벨을 제한하지는 않습니다. 좌클릭으로 올리고 우클릭으로 내립니다.\n"
                + "조건 무시 칸: 배치 조건을 무시하는 칸을 요구합니다. 좌표 고정은 아닙니다. 양보도 효과 끄기나 침 연결 금지는 아닙니다.").ToString();
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
            var canSupport = SupportTargetCandidates();

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
                        existing.LowestLevel = Mathf.Min(existing.LowestLevel, level);
                        continue;
                    }

                    plan.Names.TryGetValue(pair.Key, out var name);
                    var entry = new Entry
                    {
                        EntityId = entityId,
                        Name = string.IsNullOrEmpty(name) ? "아티팩트" : name,
                        Count = 1,
                        Level = level,
                        LowestLevel = level,
                        Selected = _prefs.IsPinned(entityId),
                        Mark = PinMark(_prefs.PinLevel(entityId)),
                        Held = _prefs.IsHeld(entityId),
                        Retained = _prefs.IsRetained(entityId),
                        AllowDeactivation = _prefs.IsDeactivationAllowed(entityId),
                        LevelCap = _prefs.LevelCap(entityId),
                        SupportTarget = _prefs.IsSupportTarget(entityId),
                        CanSupport = canSupport.Contains(entityId),
                    };
                    found[entityId] = entry;
                    order.Add(entityId);
                }
            }

            // 가방에 없는데 지정돼 있는 것도 보여야 푼다. 다른 판에서 지정한 것이 남아 있는 경우다.
            // 가져온 빌드의 아티팩트도 같이 보인다 - 무엇을 아직 못 모았는지가 곧 살 목록이다.
            var favorites = new HashSet<int>(_prefs.Preset()?.FavoriteCharms ?? new List<int>());
            var listed = _prefs.PinnedLevels.Keys.Concat(_prefs.HeldCharms).Concat(_prefs.RetainedCharms)
                .Concat(_prefs.DeactivationAllowed).Concat(_prefs.LevelCaps.Keys).Concat(_prefs.SupportTargets)
                .Concat(favorites).Distinct().ToList();
            foreach (var entityId in listed)
            {
                if (found.ContainsKey(entityId)) continue;

                var definition = _context.Catalog?.Charm(entityId);
                found[entityId] = new Entry
                {
                    EntityId = entityId,
                    Name = definition != null
                        ? Naming.Of(definition.Names, definition.Id, "아티팩트")
                        : "아티팩트 #" + entityId,
                    Detail = favorites.Contains(entityId) ? "빌드 · 가방에 없음" : "가방에 없음",
                    Selected = _prefs.IsPinned(entityId),
                    Mark = PinMark(_prefs.PinLevel(entityId)),
                    Held = _prefs.IsHeld(entityId),
                    Retained = _prefs.IsRetained(entityId),
                    AllowDeactivation = _prefs.IsDeactivationAllowed(entityId),
                    LevelCap = _prefs.LevelCap(entityId),
                    SupportTarget = _prefs.IsSupportTarget(entityId),
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
                    entry.Detail = (favorites.Contains(entityId) ? "빌드  " : "") +
                                   Explain.LevelRange(entry.LowestLevel, entry.Level) +
                                   (entry.Count > 1 ? "  x" + entry.Count : "");
                }
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// 침·모래시계가 강화할 수 있는 아티팩트. 가방에 그런 아티팩트가 없으면 비어 있고, 그러면
        /// 줄에 버튼도 걸리지 않는다 - 고를 것이 없는 지정을 띄워 두면 이름 자리만 좁아진다.
        /// </summary>
        private HashSet<int> SupportTargetCandidates()
        {
            var candidates = new HashSet<int>();
            var items = _context.Snapshot?.Inventory?.Items;
            if (items == null) return candidates;

            var needle = false;
            var magicHelper = false;
            foreach (var item in items)
            {
                var definition = _context.Catalog?.Charm(item.DefinitionId);
                if (definition == null) continue;
                if (PositionalWorth.IsNeedle(definition)) needle = true;
                if (definition.MagicSupport != null) magicHelper = true;
            }
            if (!needle && !magicHelper) return candidates;

            foreach (var item in items)
            {
                var definition = _context.Catalog?.Charm(item.DefinitionId);
                if (definition == null) continue;
                if (needle && (item.IsAttackable ?? definition.IsAttackable) || magicHelper && definition.IsMagic)
                    candidates.Add(item.DefinitionId);
            }
            return candidates;
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

        private void ResetBuild()
        {
            _prefs.ResetBuild();
            _presetMessage = "";
            _page = 0;
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
            public int LowestLevel;

            /// <summary>제한 해제 칸에 고정돼 있는가. 아티팩트 줄에만 뜻이 있다.</summary>
            public bool Held;

            /// <summary>침·모래시계가 강화할 대상으로 지정됐는가.</summary>
            public bool SupportTarget;

            /// <summary>이 가방에서 침·모래시계의 대상이 될 수 있는가. 아니면 버튼을 걸지 않는다.</summary>
            public bool CanSupport;
            public int LevelCap;
            public bool Retained;
            public bool AllowDeactivation;
        }

        /// <summary>목록 한 줄. 줄 전체가 눌린다.</summary>
        private sealed class Row
        {
            private readonly TextMeshProUGUI _name;
            private readonly TextMeshProUGUI _detail;
            private readonly TextMeshProUGUI _hold;
            private readonly TextMeshProUGUI _cap;
            private readonly TextMeshProUGUI _support;
            private readonly TextMeshProUGUI _retain;
            private readonly TextMeshProUGUI _deactivation;
            private readonly Image _background;
            private readonly RectTransform _options;
            private readonly LayoutElement _size;
            private readonly float _base;
            private Entry _entry;

            public Row(
                RectTransform parent, NativeSkin skin, float b, Action<Entry> onClick, Action<Entry> onHold,
                Action<Entry> onRetain, Action<Entry> onDeactivation, Action<Entry> onSupport, Action<Entry, int> onCap)
            {
                _base = b;
                _background = Widgets.ClickableRow(
                    "Entry", parent, NativeSkin.SlotFill, () => onClick(_entry));
                var rect = _background.rectTransform;
                var pad = Mathf.RoundToInt(b * 0.4f);
                Widgets.Column(rect, b * 0.1f, new RectOffset(pad, pad, 0, 0));
                _size = Widgets.Fixed(rect, b * 2.9f);

                var heading = Widgets.Rect("Heading", rect);
                Widgets.Row(heading, b * 0.4f);
                Widgets.Fixed(heading, b * 1.5f);

                _name = Widgets.Label("Name", heading, skin, b * 0.95f, NativeSkin.Text);
                Widgets.Fixed(_name.rectTransform, b * 1.5f).flexibleWidth = 1;

                _detail = Widgets.Label(
                    "Detail", heading, skin, b * 0.8f, NativeSkin.TextDim, TextAlignmentOptions.MidlineRight);
                Widgets.Fixed(_detail.rectTransform, b * 1.5f, b * 7f);

                _options = Widgets.Rect("Options", rect);
                Widgets.Row(_options, b * 0.3f);
                Widgets.Fixed(_options, b * 1.3f);
                _retain = Widgets.Clickable("Retain", _options, skin, b * 0.75f, NativeSkin.TextDim, () => onRetain(_entry));
                _retain.text = "사용 유지";
                Widgets.Fixed(_retain.rectTransform, b * 1.3f, b * 4.2f);
                _deactivation = Widgets.Clickable("Deactivation", _options, skin, b * 0.75f, NativeSkin.TextDim, () => onDeactivation(_entry));
                _deactivation.text = "끄기 허용";
                Widgets.Fixed(_deactivation.rectTransform, b * 1.3f, b * 4.2f);
                _support = Widgets.Clickable("Support", _options, skin, b * 0.75f, NativeSkin.TextDim, () => onSupport(_entry));
                _support.text = "침·모래시계·별조각 우선";
                Widgets.Fixed(_support.rectTransform, b * 1.3f, b * 10.5f);
                _cap = Widgets.Clickable("Cap", _options, skin, b * 0.75f, NativeSkin.TextDim, () => onCap(_entry, 1));
                Widgets.Fixed(_cap.rectTransform, b * 1.3f, b * 5.2f);
                _hold = Widgets.Clickable("Hold", _options, skin, b * 0.75f, NativeSkin.TextDim, () => onHold(_entry));
                _hold.text = "조건 무시 칸";
                _hold.alignment = TextAlignmentOptions.MidlineRight;
                Widgets.Fixed(_hold.rectTransform, b * 1.3f, b * 6f);
            }

            public Entry Entry => _entry;
            public RectTransform Rect => _background.rectTransform;

            /// <summary>레벨 제한 버튼의 자리. 우클릭을 여기서 받아 한 단계 내린다.</summary>
            public RectTransform CapRect => _cap.rectTransform;

            public void Show(Entry entry)
            {
                _entry = entry;
                var isCharm = entry.EntityId != 0;
                _size.minHeight = _size.preferredHeight = _base * (isCharm ? 2.9f : 1.5f);
                Widgets.SetActive(_options, isCharm);
                _name.text = (entry.Mark ?? (entry.Selected ? "● " : "○ ")) + entry.Name;
                _name.color = entry.Selected ? NativeSkin.Mint : NativeSkin.Text;
                _detail.text = entry.Detail;
                _retain.color = entry.Retained ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_retain, entry.EntityId != 0);
                _deactivation.color = entry.AllowDeactivation ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_deactivation, entry.EntityId != 0);
                _support.color = entry.SupportTarget ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_support, entry.EntityId != 0 && (entry.CanSupport || entry.SupportTarget));
                _cap.text = entry.LevelCap > 0 ? "목표 " + entry.LevelCap + "레벨" : "목표 레벨";
                _cap.color = entry.LevelCap > 0 ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_cap, entry.EntityId != 0);
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
