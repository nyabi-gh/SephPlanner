using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SephPlanner.Core.Combat;
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
            Combat,
            Magic,
            Details,
        }

        private readonly PluginPreferences _prefs;
        private readonly Func<BuildContext> _source;

        private Tab _tab = Tab.Combos;
        private int _page;
        private string _presetMessage = "";
        private string _combatDetail = "";
        private readonly Dictionary<Tab, TextMeshProUGUI> _tabs = new Dictionary<Tab, TextMeshProUGUI>();

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
                _rows.Add(new Row(list, Skin, Base, entry => Toggle(entry, 1), ToggleHold, ToggleRetain, ToggleDeactivation));

            BuildPager(content);
        }

        protected override void Cleared()
        {
            _rows.Clear();
            _entries.Clear();
            _tabs.Clear();
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
            Tab_(row, "전투", Tab.Combat);
            Tab_(row, "마법", Tab.Magic);
            Tab_(row, "DPS 내역", Tab.Details);
        }

        private TextMeshProUGUI Tab_(RectTransform row, string label, Tab tab)
        {
            var text = Widgets.Clickable(
                "Tab_" + label, row, Skin, S(1f), NativeSkin.TextDim,
                () =>
                {
                    if (_tab == tab) return;

                    _tab = tab;
                    _combatDetail = "";

                    // 쪽 번호는 목록마다 따로다. 이어받으면 세 쪽짜리에서 넘어온 뒤 빈 쪽이 뜬다.
                    _page = 0;
                    Refresh();
                });
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            Widgets.Fixed(text.rectTransform, S(1.4f), S(5.4f));
            _tabs[tab] = text;
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
            foreach (var tab in _tabs) tab.Value.color = _tab == tab.Key ? NativeSkin.Mint : NativeSkin.TextDim;
            _comboTab.color = _tab == Tab.Combos ? NativeSkin.Mint : NativeSkin.TextDim;
            _charmTab.color = _tab == Tab.Charms ? NativeSkin.Mint : NativeSkin.TextDim;

            if (_tab == Tab.Combos)
            {
                _note.text = _prefs.Combat.PrioritizeBuild
                    ? "빌드 우선: 지정 콤보와 프리셋 즐겨찾기를 DPS보다 먼저 반영합니다. DPS가 낮은 배치·후보를 고를 수 있습니다."
                    : "DPS 우선: 지정은 보관하고 계산 가능한 콤보 피해만 DPS에 반영합니다. 강제 우선을 원하면 빌드 우선을 켜세요.";
                _note.color = _context.Recommendations ? NativeSkin.TextDim : NativeSkin.Amber;
                return;
            }

            _note.text = _tab == Tab.Combat ? (_combatDetail.Length > 0 ? _combatDetail : "누르면 증가·다음, 우클릭하면 감소·이전입니다. 동작 시간은 공격 속도 적용 전 비교용 추정값입니다.") :
                _tab == Tab.Magic ? "마법은 위에서부터 우선 사용하며 마나를 공유합니다. 누르면 위로, 우클릭하면 아래로 옮깁니다." :
                _tab == Tab.Details ? (_combatDetail.Length > 0 ? _combatDetail : "현재·추천 DPS와 미지원 변경을 비교합니다. 줄을 누르면 설명을 봅니다. 미지원 변경의 자동 적용은 기본 제한됩니다.") :
                "예상 DPS로 배치합니다. 기존 강화·양보 배수는 적용하지 않습니다. 사용 유지·끄기 허용·제한 해제 칸 고정을 선택할 수 있습니다.";
            _note.color = NativeSkin.TextDim;
        }

        /// <summary>우클릭은 단계를 내린다. 아티팩트 줄에서만 뜻이 있다.</summary>
        public override void RightClick(Vector2 cursor)
        {
            if (!IsOpen || _tab == Tab.Combos) return;

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

        private void ToggleRetain(Entry entry)
        {
            if (entry?.OnToggle != null) { entry.OnToggle(); Refresh(); return; }
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

        private void ToggleHold(Entry entry)
        {
            if (entry == null || entry.EntityId == 0) return;

            _prefs.ToggleHold(entry.EntityId);
            Refresh();
        }

        private void Toggle(Entry entry, int direction)
        {
            if (entry == null) return;
            if (entry.Change != null) entry.Change(direction);
            else if (entry.EntityId != 0) return;
            else if (direction > 0) _prefs.TogglePriority(entry.Key);
            else return;

            Refresh();
        }

        private void CollectEntries()
        {
            _entries.Clear();
            if (_tab == Tab.Combos) CollectCombos();
            else if (_tab == Tab.Charms) CollectCharms();
            else if (_tab == Tab.Combat) CollectCombat();
            else if (_tab == Tab.Magic) CollectMagic();
            else CollectCombatDetails();
        }

        private void CollectCombat()
        {
            var scenario = _prefs.Combat;
            Setting("추천 기준", scenario.PrioritizeBuild ? "빌드 우선 → DPS" : "보호 조건 안에서 DPS 우선", (copy, _) => copy.PrioritizeBuild = !copy.PrioritizeBuild);
            Setting("미지원 변경 자동 적용", scenario.AllowUnsupportedChanges ? "별도 허용됨" : "제한 · 권장", (copy, _) => copy.AllowUnsupportedChanges = !copy.AllowUnsupportedChanges);
            Setting("대상 수", scenario.TargetCount + (scenario.TargetCount == 1 ? " · 단일" : " · 다수"), (copy, delta) => copy.TargetCount = Math.Max(1, Math.Min(100, copy.TargetCount + delta)));
            Setting("추가 대상 적중 비율", scenario.AdditionalTargetFraction.ToString("P0") + " · 추정", (copy, delta) => copy.AdditionalTargetFraction = Math.Round(Math.Max(0, Math.Min(1, copy.AdditionalTargetFraction + delta * 0.1)), 1));
            Setting("비교 시간", scenario.DurationSeconds + "초", (copy, delta) => copy.DurationSeconds = Math.Max(1, Math.Min(600, copy.DurationSeconds + delta)));
            Setting("초반·후반 구간 길이", scenario.ComparisonWindowSeconds + "초", (copy, delta) => copy.ComparisonWindowSeconds = Math.Max(1, Math.Min(600, copy.ComparisonWindowSeconds + delta)));
            Setting("시작 마나", scenario.InitialManaFraction.ToString("P0"), (copy, delta) => copy.InitialManaFraction = Math.Round(Math.Max(0, Math.Min(1, copy.InitialManaFraction + delta * 0.1)), 1));
            Setting("시작 충전", scenario.InitialChargeFraction.ToString("P0"), (copy, delta) => copy.InitialChargeFraction = Math.Round(Math.Max(0, Math.Min(1, copy.InitialChargeFraction + delta * 0.1)), 1));
            Setting("시간·자원 프리셋 · 단기", "30초 · 마나·충전 가득", (copy, _) =>
            {
                copy.DurationSeconds = 30;
                copy.InitialManaFraction = copy.InitialChargeFraction = 1;
            });
            Setting("시간·자원 프리셋 · 장기", "120초 · 마나·충전 비움", (copy, _) =>
            {
                // 장기 비교를 위한 선택값이며 게임의 전투 길이 실측값이 아니다.
                copy.DurationSeconds = 120;
                copy.InitialManaFraction = copy.InitialChargeFraction = 0;
            });
            var patterns = new[] { new[] { CombatActionKind.Basic }, new[] { CombatActionKind.Dash }, new[] { CombatActionKind.Special },
                new[] { CombatActionKind.Basic, CombatActionKind.Dash }, new[] { CombatActionKind.Basic, CombatActionKind.Special },
                new[] { CombatActionKind.Basic, CombatActionKind.Dash, CombatActionKind.Special }, Array.Empty<CombatActionKind>() };
            Setting("공격 반복 순서", scenario.WeaponSequence.Count == 0 ? "무기 공격 안 함" : string.Join("→", scenario.WeaponSequence.Select(ActionName)), (copy, delta) =>
            {
                var index = Array.FindIndex(patterns, pattern => pattern.SequenceEqual(copy.WeaponSequence));
                copy.WeaponSequence = patterns[(index + delta + patterns.Length) % patterns.Length].ToList();
            });
            _entries.Add(new Entry { Name = "복사한 공격 순서 붙여넣기", Detail = "일반,일반,대시", Mark = "", Change = _ => PasteAttackSequence() });
            Setting("일반 공격 시간", scenario.BasicSeconds.ToString("0.##") + "초", (copy, delta) => copy.BasicSeconds = Seconds(copy.BasicSeconds, delta));
            Setting("대시 공격 시간", scenario.DashSeconds.ToString("0.##") + "초", (copy, delta) => copy.DashSeconds = Seconds(copy.DashSeconds, delta));
            Setting("특수 공격 시간", scenario.SpecialSeconds.ToString("0.##") + "초", (copy, delta) => copy.SpecialSeconds = Seconds(copy.SpecialSeconds, delta));
            Setting("마법 시전 시간", scenario.CastingSeconds.ToString("0.##") + "초", (copy, delta) => copy.CastingSeconds = Seconds(copy.CastingSeconds, delta));
            _entries.Add(new Entry { Name = "복사한 실측 간격으로 보정", Detail = "일반=0.42,대시=0.8", Mark = "", Change = _ => PasteMeasuredTiming() });
            Detail("실측 간격 보정 상태", scenario.MeasuredActionSeconds.Count == 0 ? "없음" :
                _context.Snapshot?.Run?.Combat is { } captured && CombatTiming.Matches(captured, scenario) ? "현재 무기에 적용" : "다른 무기 · 미적용",
                "마법·이동·대기 없이 동일 동작을 반복한 평균 발동 간격을 입력하세요. 일반 공격은 전체 연속 공격을 여러 번 반복해 측정합니다. 현재 공격 속도를 역산하며 보정된 동작은 위의 기본 시간보다 먼저 사용합니다.");
            Setting("실측 간격 보정 해제", "기본 동작 시간 사용", (copy, _) =>
            {
                copy.MeasuredActionSeconds.Clear();
                copy.MeasuredWeaponKey = "";
            });
            Setting("마법 사용", scenario.UseMagic ? "켜짐" : "꺼짐", (copy, _) => copy.UseMagic = !copy.UseMagic);
            Setting("현재 활성·연결 보호", scenario.PreserveActivation ? "켜짐" : "꺼짐", (copy, _) => copy.PreserveActivation = !copy.PreserveActivation);
            Detail("보호 범위", "활성·사용·연결", "보호는 체력·방어력·이동 능력의 최저치를 보장하지 않습니다. 미지원 변경 허용은 활성·사용 유지 검증을 해제하지 않습니다.");
            Setting("대상 종류", scenario.Boss ? "정예·보스" : "일반", (copy, _) => copy.Boss = !copy.Boss);
            foreach (var target in new[] { ("DAMAGEREDUCTION", "대상 방어력"), ("PHYSICALDEFENSE", "대상 물리 저항"), ("FIREDEFENSE", "대상 화염 저항"), ("ICEDEFENSE", "대상 냉기 저항"), ("LIGHTNINGDEFENSE", "대상 번개 저항") })
            {
                scenario.TargetStats.TryGetValue(target.Item1, out var value);
                Setting(target.Item2, value.ToString(), (copy, delta) =>
                {
                    copy.TargetStats.TryGetValue(target.Item1, out var current);
                    copy.TargetStats[target.Item1] = Math.Max(0, current + delta * 5);
                });
            }
            _entries.Add(new Entry { Name = "전투 조건 기본값 복원", Detail = "30초 · 자원 가득", Mark = "", Change = _ => _prefs.SetCombat(new CombatScenario()) });
        }

        private void Setting(string name, string value, Action<CombatScenario, int> change) => _entries.Add(new Entry
        {
            Name = name,
            Detail = value,
            Mark = "",
            Change = delta =>
            {
                var copy = _prefs.Combat.Copy();
                change(copy, delta);
                _prefs.SetCombat(copy);
            },
        });

        private static double Seconds(double value, int delta) => Math.Round(Math.Max(0.1, Math.Min(60, value + delta * 0.1)), 1);
        private static string ActionName(CombatActionKind action) => action == CombatActionKind.Basic ? "일반" : action == CombatActionKind.Dash ? "대시" : "특수";

        private void PasteAttackSequence()
        {
            var sequence = new List<CombatActionKind>();
            foreach (var word in GUIUtility.systemCopyBuffer.Split(new[] { ',', ' ', '→', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word != "일반" && word != "대시" && word != "특수")
                {
                    _presetMessage = "공격 순서는 일반, 대시, 특수를 쉼표로 나눠 복사하세요.";
                    return;
                }
                sequence.Add(word == "일반" ? CombatActionKind.Basic : word == "대시" ? CombatActionKind.Dash : CombatActionKind.Special);
            }
            var copy = _prefs.Combat.Copy();
            copy.WeaponSequence = sequence;
            _prefs.SetCombat(copy);
            _presetMessage = "공격 반복 순서를 적용했습니다.";
        }

        private void PasteMeasuredTiming()
        {
            if (_context.Snapshot?.Run?.Combat is not { } snapshot)
            {
                _presetMessage = "전투 자료를 읽은 뒤 실측 간격을 적용할 수 있습니다.";
                return;
            }
            try
            {
                _prefs.SetCombat(CombatTiming.Calibrate(snapshot, _prefs.Combat, GUIUtility.systemCopyBuffer));
                _presetMessage = "현재 무기·공격 속도를 기준으로 실측 간격을 보정했습니다.";
            }
            catch (ArgumentException error) { _presetMessage = error.Message; }
            catch (InvalidOperationException error) { _presetMessage = error.Message; }
        }

        private void CollectMagic()
        {
            var ids = (_context.Snapshot?.Inventory?.Items ?? new List<PlacedItem>()).Select(item => item.DefinitionId)
                .Where(id => _context.Catalog?.Charm(id)?.IsMagic == true).Distinct()
                .OrderBy(id => _prefs.Combat.MagicPriority.Contains(id) ? _prefs.Combat.MagicPriority.IndexOf(id) : int.MaxValue).ThenBy(id => id).ToList();
            foreach (var id in ids)
            {
                var definition = _context.Catalog.Charm(id);
                _entries.Add(new Entry
                {
                    Name = Naming.Of(definition.Names, definition.Id, "마법"),
                    Mark = (ids.IndexOf(id) + 1) + ". ",
                    Detail = definition.Combat.Attacks.Count > 0 ? "예상 피해 반영" : "피해 동작 미지원",
                    Retained = !_prefs.Combat.DisabledMagic.Contains(id),
                    ToggleLabel = "사용",
                    OnToggle = () =>
                    {
                        var copy = _prefs.Combat.Copy();
                        if (!copy.DisabledMagic.Remove(id)) copy.DisabledMagic.Add(id);
                        _prefs.SetCombat(copy);
                    },
                    Change = delta =>
                    {
                        var order = new List<int>(ids);
                        var index = order.IndexOf(id);
                        order.RemoveAt(index);
                        order.Insert(Math.Max(0, Math.Min(order.Count, index - delta)), id);
                        var copy = _prefs.Combat.Copy();
                        copy.MagicPriority = order.Concat(copy.MagicPriority.Where(previous => !order.Contains(previous))).ToList();
                        _prefs.SetCombat(copy);
                    },
                });
            }
        }

        private void CollectCombatDetails()
        {
            var current = _context.Plan?.Current.Combat;
            var best = _context.Plan?.Best.Combat;
            if (best == null) return;
            Detail("현재 → 추천 예상 DPS", $"{current?.Dps:0.##} → {best.Dps:0.##}", "DPS는 선택한 시간 동안 계산한 총 피해를 시간으로 나눈 값입니다. 추천 순위는 이 전체 구간 DPS로 정합니다.");
            Detail("추천 기준", _context.Plan.PrioritizeBuild ? "빌드 우선 → DPS" : "보호 조건 안에서 DPS 우선", "빌드 우선일 때만 지정 콤보와 프리셋 즐겨찾기가 DPS보다 앞섭니다. 계산 가능한 콤보 효과는 두 모드 모두 피해에 포함됩니다.");
            Detail("비교 조건", $"{best.DurationSeconds:0.##}초 / {best.TargetCount}명", $"첫 대상은 적중한다고 가정합니다. 공격 대상 상한 안에서 추가 대상의 {best.AdditionalTargetFraction:P0}에 적중하는 기대값입니다. 적의 사망·이동은 재현하지 않습니다.");
            Detail($"초반 {best.ComparisonWindowSeconds:0.##}초 DPS", $"{current?.OpeningDps:0.##} → {best.OpeningDps:0.##}", "선택한 전투의 시작 구간입니다. 시작 자원에 따른 집중 피해를 비교합니다.");
            Detail($"후반 {best.ComparisonWindowSeconds:0.##}초 DPS", $"{current?.EndingDps:0.##} → {best.EndingDps:0.##}", "같은 전투의 마지막 구간입니다. 아직 자원이 남거나 긴 재사용 대기가 있으면 안정된 지속 DPS와 다를 수 있습니다. 짧은 전투에서는 초반 구간과 겹칠 수 있습니다.");
            Detail("자원 비우고 시작한 DPS", $"{current?.EmptyStartDps:0.##} → {best.EmptyStartDps:0.##}", "마나·충전만 0으로 바꾼 별도 전투입니다. 시간·공격 순서·대상 조건은 같습니다. 회복과 충전이 시작되는 과정을 포함하며 무한 시간의 지속 DPS는 아닙니다.");
            Detail("미지원 변경 자동 적용", _context.Plan.AllowUnsupportedChanges ? "별도 허용됨" : "제한 · 권장", "계산 누락이 존재한다는 이유만으로 막지는 않습니다. 미지원 아이템의 상태·주변·지원 연결 또는 미지원 콤보 수량이 달라질 때 자동 적용을 제한합니다. 추천은 계속 표시합니다.");
            foreach (var message in _context.Plan.UnsupportedChangeWarnings)
                Detail("미지원 변경 · " + message, _context.Plan.AllowUnsupportedChanges ? "허용됨" : "자동 적용 제한", message);
            Detail("추천 총 피해 / 남은 마나", $"{best.TotalDamage:0.##} / {best.RemainingMana:0.##}", "미반영 효과는 아래 목록에서 확인하세요.");
            foreach (var part in best.Contributions.OrderByDescending(part => part.Damage))
                Detail(part.Name, $"{part.Damage / best.DurationSeconds:0.##} DPS", $"{part.Name}: {part.Uses}회 사용, 총 피해 {part.Damage:0.##}");
            foreach (var message in (current?.Unsupported ?? new List<string>()).Concat(best.Unsupported).Distinct())
                Detail("계산 누락 · " + message, "눌러서 보기", message);
            foreach (var offer in _context.Plan.Offers.Where(offer => offer.Preview?.Combat != null))
                foreach (var message in offer.Preview.Combat.Unsupported.Except(best.Unsupported))
                    Detail(offer.Candidate.Name + " · " + message, "획득 시 누락", message);
        }

        private void Detail(string name, string detail, string message) => _entries.Add(new Entry
        { Name = name, Detail = detail, Mark = "", Change = _ => _combatDetail = message });

        /// <summary>
        /// 지금 세어져 있는 콤보와, 개수가 0이 되어도 지정을 풀 수 있도록 이미 지정된 카테고리를
        /// 함께 보여준다. 순서는 개수 내림차순이되 같으면 식별자로 갈라 - 스냅샷마다 순서가
        /// 흔들리면 같은 자리를 누르려다 다른 것을 누르게 된다.
        /// </summary>
        private void CollectCombos()
        {
            Setting("추천 기준", _prefs.Combat.PrioritizeBuild ? "빌드 우선 → DPS" : "DPS 우선 · 지정 보관", (copy, _) => copy.PrioritizeBuild = !copy.PrioritizeBuild);
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
                        Selected = _prefs.IsRetained(entityId) || _prefs.IsHeld(entityId),
                        Mark = "",
                        Held = _prefs.IsHeld(entityId),
                        Retained = _prefs.IsRetained(entityId),
                        AllowDeactivation = _prefs.IsDeactivationAllowed(entityId),
                    };
                    found[entityId] = entry;
                    order.Add(entityId);
                }
            }

            // 가방에 없는데 지정돼 있는 것도 보여야 푼다. 다른 판에서 지정한 것이 남아 있는 경우다.
            // 가져온 빌드의 아티팩트도 같이 보인다 - 무엇을 아직 못 모았는지가 곧 살 목록이다.
            var favorites = new HashSet<int>(_prefs.Preset()?.FavoriteCharms ?? new List<int>());
            var listed = _prefs.HeldCharms.Concat(_prefs.RetainedCharms)
                .Concat(_prefs.DeactivationAllowed).Concat(favorites).Distinct().ToList();
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
                    Selected = _prefs.IsRetained(entityId) || _prefs.IsHeld(entityId),
                    Mark = "",
                    Held = _prefs.IsHeld(entityId),
                    Retained = _prefs.IsRetained(entityId),
                    AllowDeactivation = _prefs.IsDeactivationAllowed(entityId),
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
            public Action<int> Change;
            public Action OnToggle;
            public string ToggleLabel;
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
            public bool Retained;
            public bool AllowDeactivation;
        }

        /// <summary>목록 한 줄. 줄 전체가 눌린다.</summary>
        private sealed class Row
        {
            private readonly TextMeshProUGUI _name;
            private readonly TextMeshProUGUI _detail;
            private readonly TextMeshProUGUI _hold;
            private readonly TextMeshProUGUI _retain;
            private readonly TextMeshProUGUI _deactivation;
            private readonly Image _background;
            private Entry _entry;

            public Row(
                RectTransform parent, NativeSkin skin, float b, Action<Entry> onClick, Action<Entry> onHold,
                Action<Entry> onRetain, Action<Entry> onDeactivation)
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
                _retain.color = entry.Retained ? NativeSkin.Mint : NativeSkin.TextDim;
                _retain.text = entry.ToggleLabel ?? "사용 유지";
                Widgets.SetActive(_retain, entry.EntityId != 0 || entry.OnToggle != null);
                _deactivation.color = entry.AllowDeactivation ? NativeSkin.Mint : NativeSkin.TextDim;
                Widgets.SetActive(_deactivation, entry.EntityId != 0);
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
