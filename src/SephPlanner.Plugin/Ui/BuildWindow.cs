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
            _presetClear.text = "지우기";
            Widgets.Fixed(_presetClear.rectTransform, S(1.4f), S(4f));

            var reset = Widgets.Clickable(
                "ResetBuild", row, Skin, S(0.95f), NativeSkin.TextDim, ResetBuild);
            reset.text = "빌드 초기화";
            Widgets.Fixed(reset.rectTransform, S(1.4f), S(7f));

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
                    ? "선택한 콤보를 추천과 열쇠·종이·북향의 침 배치에 우선 반영합니다. 여러 개면 진행과 배치 가치를 비교합니다."
                    : "추천은 꺼져 있지만 선택한 콤보는 열쇠·종이·북향의 침 배치에 우선 반영합니다.";
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

                // 제한 단추 위에서는 그 제한을 내린다. 줄 전체의 우클릭은 지금처럼 ★ 를 내린다.
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
            return note.Append("해제로 강화 칸을 양보합니다. 기본은 활성 보존이며, 끄기 허용을 켠 아이템만 점수 이득을 위해 끕니다. 사용 유지는 끄기 허용보다 우선하고 지원 연결·빼기·교체도 보호합니다. 고정은 배치 조건을 무시하는 칸을 요구합니다. 레벨 제한은 그 레벨까지만 값으로 쳐서 남는 레벨을 다른 아티팩트에 돌립니다(우클릭으로 되돌림). 대상은 북향의 침과 빛나는 모래시계가 그 아티팩트를 강화하게 합니다(점수보다 우선하며, 닿을 수 없으면 그 지정만 무시하고 알립니다).").ToString();
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
                                   (entry.Level > 0 ? "+" + entry.Level : entry.Level.ToString()) +
                                   (entry.Count > 1 ? "  x" + entry.Count : "");
                }
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// 침·모래시계가 강화할 수 있는 아티팩트. 가방에 그런 아티팩트가 없으면 비어 있고, 그러면
        /// 줄에 단추도 걸리지 않는다 - 고를 것이 없는 지정을 띄워 두면 이름 자리만 좁아진다.
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

            /// <summary>제한 해제 칸에 고정돼 있는가. 아티팩트 줄에만 뜻이 있다.</summary>
            public bool Held;

            /// <summary>침·모래시계가 강화할 대상으로 지정됐는가.</summary>
            public bool SupportTarget;

            /// <summary>이 가방에서 침·모래시계의 대상이 될 수 있는가. 아니면 단추를 걸지 않는다.</summary>
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
            private Entry _entry;

            public Row(
                RectTransform parent, NativeSkin skin, float b, Action<Entry> onClick, Action<Entry> onHold,
                Action<Entry> onRetain, Action<Entry> onDeactivation, Action<Entry> onSupport, Action<Entry, int> onCap)
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
                _retain = Widgets.Clickable("Retain", rect, skin, b * 0.8f, NativeSkin.TextDim, () => onRetain(_entry));
                _retain.text = "사용 유지";
                Widgets.Fixed(_retain.rectTransform, b * 1.5f, b * 4.2f);
                _deactivation = Widgets.Clickable("Deactivation", rect, skin, b * 0.8f, NativeSkin.TextDim, () => onDeactivation(_entry));
                _deactivation.text = "끄기 허용";
                Widgets.Fixed(_deactivation.rectTransform, b * 1.5f, b * 4.2f);
                // 침·모래시계가 있는 가방에서, 그 대상이 될 수 있는 줄에만 걸린다. 이름 자리는
                // 남는 폭이라, 단추가 하나 늘 때마다 이름이 그만큼 잘린다. 같은 이유로 이름도 짧다.
                _support = Widgets.Clickable("Support", rect, skin, b * 0.8f, NativeSkin.TextDim, () => onSupport(_entry));
                _support.text = "대상";
                Widgets.Fixed(_support.rectTransform, b * 1.5f, b * 2.2f);
                _cap = Widgets.Clickable("Cap", rect, skin, b * 0.8f, NativeSkin.TextDim, () => onCap(_entry, 1));
                Widgets.Fixed(_cap.rectTransform, b * 1.5f, b * 4.2f);
                _hold = Widgets.Clickable("Hold", rect, skin, b * 0.8f, NativeSkin.TextDim, () => onHold(_entry));
                _hold.text = "고정";
                _hold.alignment = TextAlignmentOptions.MidlineRight;
                Widgets.Fixed(_hold.rectTransform, b * 1.5f, b * 2.2f);
            }

            public Entry Entry => _entry;
            public RectTransform Rect => _background.rectTransform;

            /// <summary>레벨 제한 단추의 자리. 우클릭을 여기서 받아 한 단계 내린다.</summary>
            public RectTransform CapRect => _cap.rectTransform;

            public void Show(Entry entry)
            {
                _entry = entry;
                _name.text = (entry.Mark ?? (entry.Selected ? "● " : "○ ")) + entry.Name;
                _name.color = entry.Selected ? NativeSkin.Mint : NativeSkin.Text;
                _detail.text = entry.Detail;
                _retain.color = entry.Retained ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_retain, entry.EntityId != 0);
                _deactivation.color = entry.AllowDeactivation ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_deactivation, entry.EntityId != 0);
                _support.color = entry.SupportTarget ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_support, entry.EntityId != 0 && (entry.CanSupport || entry.SupportTarget));
                _cap.text = entry.LevelCap > 0 ? "레벨 " + entry.LevelCap : "레벨 제한";
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
