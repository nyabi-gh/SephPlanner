using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using SephPlanner.Core.Runtime;
using SephPlanner.Plugin.Ui;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 화면의 자리와 크기. 이것이 그대로면 지금 떠 있는 화면을 다시 지을 이유가 없다.
    ///
    /// 기본값은 아직 아무것도 짓지 않은 상태를 뜻한다 - 폭과 크기는 설정에서 범위가 걸려 있어
    /// 실제 화면이 0 을 가질 수 없다.
    /// </summary>
    internal readonly struct PanelLayout : IEquatable<PanelLayout>
    {
        private readonly PanelCorner _corner;
        private readonly float _marginX;
        private readonly float _marginY;
        private readonly float _width;
        private readonly float _scale;

        public PanelLayout(PanelCorner corner, float marginX, float marginY, float width, float scale)
        {
            _corner = corner;
            _marginX = marginX;
            _marginY = marginY;
            _width = width;
            _scale = scale;
        }

        public bool Equals(PanelLayout other) =>
            _corner == other._corner && _marginX == other._marginX && _marginY == other._marginY &&
            _width == other._width && _scale == other._scale;

        public override bool Equals(object obj) => obj is PanelLayout other && Equals(other);

        public override int GetHashCode() =>
            (((((int)_corner * 397) ^ _marginX.GetHashCode()) * 397 ^ _marginY.GetHashCode()) * 397
             ^ _width.GetHashCode()) * 397 ^ _scale.GetHashCode();

        public static bool operator ==(PanelLayout a, PanelLayout b) => a.Equals(b);

        public static bool operator !=(PanelLayout a, PanelLayout b) => !a.Equals(b);
    }

    /// <summary>
    /// 표시 설정과 단축키 전부. BepInEx 설정 파일이 원본이고, 설정 창(<see cref="SettingsWindow"/>)이
    /// <see cref="Rows"/>로 같은 값을 읽고 쓴다. 두 길이 같은 ConfigEntry 를 보므로 어느 쪽으로
    /// 고쳐도 어긋나지 않는다.
    ///
    /// 빌드 지정(콤보·강화 우선·프리셋 코드)은 여기가 아니라 <see cref="PluginPreferences"/>에
    /// 있다. 목록과 긴 문자열이라 한 줄짜리 설정 항목으로 담기지 않는다.
    /// </summary>
    internal sealed class PluginSettings
    {
        private const float DefaultPanelMargin = 1.5f;
        public ConfigEntry<float> PollInterval { get; }
        public ConfigEntry<KeyboardShortcut> DumpKey { get; }
        public ConfigEntry<KeyboardShortcut> InventoryDumpKey { get; }
        public ConfigEntry<float> OfferRadius { get; }

        public ConfigEntry<bool> Panel { get; }
        public ConfigEntry<PanelCorner> Corner { get; }
        public ConfigEntry<float> MarginX { get; }
        public ConfigEntry<float> MarginY { get; }
        public ConfigEntry<float> Width { get; }
        public ConfigEntry<float> Scale { get; }
        public ConfigEntry<float> Opacity { get; }
        public ConfigEntry<bool> Recommendations { get; }
        public ConfigEntry<bool> MultiplayerAutoPlace { get; }
        public ConfigEntry<bool> PadWindow { get; }
        public ConfigEntry<string> DiagnosticConsent { get; }
        public ConfigEntry<bool> DiagnosticChoiceMade { get; }
        public ConfigEntry<string> AutomaticDiagnosticConsent { get; }
        public ConfigEntry<string> AutomaticDiagnosticChoice { get; }
        private readonly Action _reviewAutomaticDiagnostics;
        public static string AutomaticDiagnosticKey => AutomaticDiagnosticPolicy.ConsentKey(new Uri(DiagnosticUploadClient.DefaultEndpoint));
        public bool AutomaticDiagnosticAllowed => AutomaticDiagnosticConsent.Value == AutomaticDiagnosticKey;
        public ConfigEntry<bool> UpdateCheck { get; }
        private readonly Action _reviewDiagnostics;

        public bool DiagnosticUploadAllowed => DiagnosticConsent.Value ==
            DiagnosticUploadClient.ConsentKey(new Uri(DiagnosticUploadClient.DefaultEndpoint));

        public ConfigEntry<KeyboardShortcut> ExpandKey { get; }
        public ConfigEntry<KeyboardShortcut> AutoPlaceKey { get; }
        public ConfigEntry<KeyboardShortcut> OpacityKey { get; }
        public ConfigEntry<KeyboardShortcut> MoveKey { get; }
        public ConfigEntry<KeyboardShortcut> HideKey { get; }
        public ConfigEntry<KeyboardShortcut> SettingsKey { get; }
        public ConfigEntry<KeyboardShortcut> BuildKey { get; }
        public ConfigEntry<KeyboardShortcut> PreviewKey { get; }

        private readonly Action<string> _log;
        private readonly ConfigFile _config;
        private readonly List<ConfigEntry<KeyboardShortcut>> _shortcuts =
            new List<ConfigEntry<KeyboardShortcut>>();

        // 오름차순이어야 설정 창에서 왼쪽이 흐리게, 오른쪽이 진하게가 된다.
        public static readonly float[] OpacitySteps = { 0.55f, 0.7f, 0.85f, 1.0f };
        private static readonly float[] ScaleSteps = { 0.8f, 0.9f, 1.0f, 1.15f, 1.3f, 1.5f };
        private static readonly float[] WidthSteps = { 18f, 22f, 26f, 30f, 36f };

        // 두 값은 오랫동안 설정 파일에만 있었다. 그런데 갱신 주기는 느린 기계가 제일 먼저
        // 손대야 하는 값이고, 추천 범위는 "왜 저 상자가 후보에 없지"의 답이다. 게임을 끄고
        // 텍스트 편집기를 열어야만 닿을 수 있는 자리에 둘 값이 아니다.
        private static readonly float[] PollSteps = { 0.15f, 0.25f, 0.5f, 1f };
        private static readonly float[] RadiusSteps = { 8f, 12f, 20f, 30f };

        public static float MinScale => ScaleSteps[0];
        public static float MaxScale => ScaleSteps[ScaleSteps.Length - 1];
        public static float MinWidth => WidthSteps[0];
        public static float MaxWidth => WidthSteps[WidthSteps.Length - 1];

        /// <summary>
        /// 범위를 붙여 묶는다. 붙여 두면 BepInEx 가 설정 파일의 값을 잘라 주고 설명에 범위도
        /// 적어 준다. 없으면 NaN 이나 0 이 그대로 들어와, 폴링 주기의 경우 매 프레임 폴링이 된다.
        /// </summary>
        private static ConfigDescription Ranged(string description, float min, float max) =>
            new ConfigDescription(description, new AcceptableValueRange<float>(min, max));

        public PluginSettings(ConfigFile config, Action<string> log, Action reviewDiagnostics, Action reviewAutomaticDiagnostics)
        {
            _config = config;
            _log = log;
            _reviewDiagnostics = reviewDiagnostics;
            _reviewAutomaticDiagnostics = reviewAutomaticDiagnostics;
            AutomaticDiagnosticConsent = config.Bind("Diagnostics", "AutomaticUploadConsent", "",
                "오류 자동 전송에 별도로 동의한 항목과 수신처. F3에서 철회할 수 있습니다.");
            AutomaticDiagnosticChoice = config.Bind("Diagnostics", "AutomaticUploadChoice", "",
                "자동 전송 안내에 답한 버전. 플러그인 버전만 바뀌면 다시 묻지 않습니다.");
            DiagnosticConsent = config.Bind("Diagnostics", "UploadConsent", "",
                "F10 진단을 비공개 서버로 전송하는 데 동의한 대상과 항목 버전. F3에서 변경합니다.");
            DiagnosticChoiceMade = config.Bind("Diagnostics", "UploadChoiceMade", false,
                "F10 진단 전송 여부를 사용자가 선택했는지 기록합니다.");
            // 진단 전송과 달리 동의 창이 없다. GitHub 에 가는 것은 "최신 판이 무엇이냐" 는 물음
            // 하나뿐이고 받는 것은 창에서 따로 묻는다. 끄는 자리는 F3 에 있다.
            UpdateCheck = config.Bind("Updates", "CheckOnStart", true,
                "게임을 켤 때 GitHub Releases 에서 새 안정판이 있는지 확인합니다. 있으면 창으로 물어본 뒤에만 받습니다.");

            PollInterval = config.Bind(
                "General", "PollIntervalSeconds", 0.25f, Ranged(
                    "인벤토리를 다시 읽는 주기(초).", 0.1f, 5f));
            DumpKey = config.Bind(
                "General", "DumpCatalogKey", new KeyboardShortcut(KeyCode.F9),
                "석판/아티팩트 데이터를 다시 덤프하고 질의 파서를 검증하는 단축키.");
            InventoryDumpKey = config.Bind(
                "General", "DumpInventoryKey", new KeyboardShortcut(KeyCode.F10),
                "인벤토리 내용을 그대로 파일로 남기는 단축키. 인식 문제를 확인할 때 쓴다.");
            OfferRadius = config.Bind(
                "General", "OfferRadius", 12f, Ranged(
                    "선택지로 볼 상자/상점까지의 거리. 넓히면 멀리 있는 것까지 추천에 들어온다.", 1f, 50f));

            Panel = config.Bind(
                "NativePanel", "Enabled", true,
                "게임 HUD 안에 점수 패널을 직접 그린다.");
            // 열쇠 이름이 예전과 다르다. 뜻이 바뀌었는데 이름을 그대로 두면 저장된 옛 값이
            // 쓰여 화면이 엉뚱한 곳으로 간다.
            Corner = config.Bind(
                "NativePanel", "Corner", PanelCorner.TopRight,
                "화면을 붙일 모서리. 게임 HUD 와 겹치면 옮긴다.");
            MarginX = config.Bind(
                "NativePanel", "MarginX", DefaultPanelMargin, Ranged(
                    "모서리에서 가로로 띄울 거리. 게임 HUD 글자 크기의 배수라 해상도가 달라도 같게 보인다.",
                    0f, 40f));
            MarginY = config.Bind(
                "NativePanel", "MarginY", DefaultPanelMargin, Ranged(
                    "모서리에서 세로로 띄울 거리. 이동 모드로 옮기면 여기에 저장된다.", 0f, 40f));
            Width = config.Bind(
                "NativePanel", "WidthScale", 26f, Ranged(
                    "화면의 가로 폭. 역시 게임 HUD 글자 크기의 배수다. 글씨가 잘리면 키운다.",
                    MinWidth, MaxWidth));
            Scale = config.Bind(
                "NativePanel", "Scale", 1.0f, Ranged(
                    "화면 전체의 크기 배율. 1 이 게임 HUD 글자와 같은 크기다.", MinScale, MaxScale));
            Opacity = config.Bind(
                "NativePanel", "Opacity", 1.0f, Ranged("화면의 불투명도(0~1).", 0.1f, 1f));
            Recommendations = config.Bind(
                "NativePanel", "Recommendations", true,
                "무엇을 집을지에 대한 후보 추천을 계산할지. 끄면 가리는 것이 아니라 계산 자체를 " +
                "건너뛴다. 점수와 배치 제안은 그대로 남는다.");

            // 기본은 꺼짐이다. 개발사가 금지한 것은 아니고, 인벤토리 동기화 구현을 바꾸는 중이라
            // 잠가 두는 편이 안전하다고 답했다(docs/LEGAL.md "받은 답변").
            MultiplayerAutoPlace = config.Bind(
                "NativePanel", "MultiplayerAutoPlace", false,
                "멀티플레이 세션에서도 자동 배치를 허용한다(실험). 방을 연 쪽이든 참가한 쪽이든 " +
                "동작한다. 동기화가 검증되지 않았고 개발사도 잠가 두는 편이 안전하다고 했으므로, " +
                "같이 하는 사람의 동의를 얻고 켠다.");

            PadWindow = config.Bind(
                "NativePanel", "PadOpensWindow", true,
                "게임패드의 View(뒤로) 버튼으로 SephPlanner 창을 엽니다. 가방·상자 같은 게임 창이 " +
                "떠 있고 지도가 닫혀 있는 동안에만 듣습니다.");

            // 게임이 쓰지 않는 키로 고른다. 게임은 수정키를 보지 않으므로 Ctrl+Alt 를 붙여도
            // 글자 키는 게임 조작을 함께 발동시킨다(docs/RESEARCH.md 의 "게임 단축키").
            // F 키는 게임이 하나도 쓰지 않으며 F9/F10 이 이미 같은 이유로 쓰이고 있다.
            ExpandKey = config.Bind(
                "NativePanel", "ExpandKey", new KeyboardShortcut(KeyCode.F7),
                "패널을 접고 펴는 단축키. 상자·상점을 열면 저절로 펼쳐진다.");
            AutoPlaceKey = config.Bind(
                "NativePanel", "AutoPlaceKey", new KeyboardShortcut(KeyCode.F8),
                "제안된 배치를 게임에 적용하는 단축키. 멀티 세션에서는 위 허용을 켜야 동작한다.");
            OpacityKey = config.Bind(
                "NativePanel", "OpacityKey", new KeyboardShortcut(KeyCode.F5),
                "불투명도를 차례로 바꾸는 단축키.");
            MoveKey = config.Bind(
                "NativePanel", "MoveKey", new KeyboardShortcut(KeyCode.F6),
                "이동 모드. 한 번 누르면 화면이 커서를 따라오고, 다시 누르면 그 자리에 고정된다.");
            SettingsKey = config.Bind(
                "NativePanel", "SettingsKey", new KeyboardShortcut(KeyCode.F3),
                "설정 창을 여는 단축키. 여는 동안에는 게임 조작이 멈추고 ESC 로도 닫힌다.");
            HideKey = config.Bind(
                "NativePanel", "HideKey", new KeyboardShortcut(KeyCode.F4),
                "화면을 통째로 숨겼다가 다시 보여준다. 숨어 있어도 계산은 계속 돌아서 다시 " +
                "켜면 곧바로 최신 배치가 뜬다.");
            BuildKey = config.Bind(
                "NativePanel", "BuildKey", new KeyboardShortcut(KeyCode.F2),
                "빌드 창을 여는 단축키. 프리셋 코드 가져오기, 밀고 있는 콤보 지정, 강화 우선 " +
                "아티팩트 지정을 여기서 한다.");
            PreviewKey = config.Bind(
                "NativePanel", "PreviewKey", new KeyboardShortcut(KeyCode.F1),
                "후보를 차례로 미리보는 단축키. 그 후보를 집었을 때의 격자를 대신 보여주고, " +
                "마지막 다음은 미리보기 없음으로 돌아온다.");

            _shortcuts.Add(ExpandKey);
            _shortcuts.Add(AutoPlaceKey);
            _shortcuts.Add(OpacityKey);
            _shortcuts.Add(MoveKey);
            _shortcuts.Add(HideKey);
            _shortcuts.Add(SettingsKey);
            _shortcuts.Add(BuildKey);
            _shortcuts.Add(PreviewKey);
            _shortcuts.Add(DumpKey);
            _shortcuts.Add(InventoryDumpKey);

            var migrated = config.Bind("General", "LegacyShortcutsMigrated", false,
                "이전 기본 단축키의 일회성 이전 완료 여부.");
            if (!migrated.Value)
            {
                Retire(ExpandKey, new KeyboardShortcut(KeyCode.P, KeyCode.LeftControl, KeyCode.LeftAlt));
                Retire(AutoPlaceKey, new KeyboardShortcut(KeyCode.Return, KeyCode.LeftControl, KeyCode.LeftAlt));
                migrated.Value = true;
            }
            foreach (var shortcut in _shortcuts)
                shortcut.SettingChanged += (_, _) => _shortcutRevision++;
            foreach (var shortcut in _shortcuts) WarnIfGameKey(shortcut);
        }

        /// <summary>
        /// 화면을 지을 때 쓰는 값들. 달라지면 지금 떠 있는 화면을 버리고 다시 짓는다.
        ///
        /// 문자열이 아니라 값 구조체인 것은 <b>프레임마다 두 번</b> 읽기 때문이다. 서식으로 짓던
        /// 때는 그때마다 실수 다섯 벌과 이어 붙인 문자열이 쓰레기로 남았는데, 이 게임은 증분 GC 가
        /// 프레임당 3ms 를 가져간다(<c>boot.config</c> 의 <c>gc-max-time-slice</c>).
        /// </summary>
        public PanelLayout Layout =>
            new PanelLayout(Corner.Value, MarginX.Value, MarginY.Value, Width.Value, Scale.Value);

        private int _shortcutRevision;

        /// <summary>
        /// 단축키 배선이 바뀔 때마다 오른다. 안내 줄이 단축키 이름을 여덟 개 짓는데 그것을
        /// 프레임마다 다시 지을 이유가 없어, 다시 지어야 할 때를 이 값으로 가른다.
        /// 설정 파일을 다시 읽어 값이 바뀌는 경우도 포함한다.
        /// </summary>
        public int ShortcutRevision => _shortcutRevision;

        /// <summary>
        /// 단축키는 누를 때마다 흐려진다. 값 목록은 오름차순이라 거꾸로 훑는다 - 진하게 켜 두고
        /// 가릴 때 한 번씩 누르는 것이 실제 쓰임이라, 그 방향이 한 번 눌러 얻는 값이 크다.
        /// </summary>
        public void CycleOpacity()
        {
            var next = Nearest(OpacitySteps, Opacity.Value) - 1;
            if (next < 0) next = OpacitySteps.Length - 1;
            Opacity.Value = OpacitySteps[next];
        }

        /// <summary>
        /// 설정 창이 그릴 줄들. 값은 여기서 읽고 여기로 쓴다 - 탭은 무엇을 고를
        /// 수 있는지만 알고, 그 값이 무슨 뜻인지는 알지 못한다.
        /// </summary>
        private List<OptionRow> _rows;

        public List<OptionRow> Rows()
        {
            return _rows ??= new List<OptionRow>
            {
                Switch("플래너 화면 표시", Panel),
                new OptionRow
                {
                    Label = "화면 기준 위치",
                    Choices = new[] { "왼쪽 위", "오른쪽 위", "왼쪽 아래", "오른쪽 아래" },
                    Read = () => (int)Corner.Value,
                    Write = i => ChangeCorner((PanelCorner)i),
                },
                Steps("크기", Scale, ScaleSteps, new[] { "80%", "90%", "100%", "115%", "130%", "150%" }),
                Steps("폭", Width, WidthSteps,
                    new[] { "아주 좁게", "좁게", "보통", "넓게", "아주 넓게" }),
                Steps("불투명도", Opacity, OpacitySteps, new[] { "55%", "70%", "85%", "100%" }),
                Switch("획득·합성·인챈트·제거 추천", Recommendations),
                Steps("주변 아이템 탐색 범위", OfferRadius, RadiusSteps,
                    new[] { "좁게", "보통", "넓게", "아주 넓게" }),
                Switch("멀티 자동 배치(실험)", MultiplayerAutoPlace),
                new OptionRow
                {
                    Label = "진단 서버 전송",
                    Choices = new[] { "꺼짐", "켜짐" },
                    Read = () => DiagnosticUploadAllowed ? 1 : 0,
                    Write = i => { if (i == 0) SetDiagnosticConsent(false); else _reviewDiagnostics(); },
                },
                new OptionRow
                {
                    Label = "오류 진단 자동 전송",
                    Choices = new[] { "꺼짐", "켜짐" },
                    Read = () => AutomaticDiagnosticAllowed ? 1 : 0,
                    Write = i => { if (i == 0) SetAutomaticDiagnosticConsent(false); else _reviewAutomaticDiagnostics(); },
                },
                Switch("시작할 때 업데이트 확인", UpdateCheck),
                Steps("가방 확인 간격", PollInterval, PollSteps,
                    new[] { "0.15초", "0.25초", "0.5초", "1초" }),
                Switch("패드 View 버튼으로 창 열기", PadWindow),
                Key("추천 화면 접기/펼치기", ExpandKey),
                Key("자동 배치", AutoPlaceKey),
                Key("후보 미리보기", PreviewKey),
                Key("빌드 설정 창", BuildKey),
                Key("불투명도 바꾸기", OpacityKey),
                Key("플래너 화면 이동", MoveKey),
                Key("화면 숨기기/보이기", HideKey),
                Key("설정 창", SettingsKey),
                Key("아이템 데이터 다시 읽기", DumpKey),
                Key("문제 진단 저장·전송", InventoryDumpKey),
            };
        }

        private void ChangeCorner(PanelCorner corner)
        {
            if (Corner.Value == corner) return;

            Corner.Value = corner;
            MarginX.Value = DefaultPanelMargin;
            MarginY.Value = DefaultPanelMargin;
            _config.Save();
        }

        public void SetAutomaticDiagnosticConsent(bool allowed)
        {
            var previousChoice = AutomaticDiagnosticChoice.Value;
            try
            {
                AutomaticDiagnosticConsent.Value = allowed ? AutomaticDiagnosticKey : "";
                AutomaticDiagnosticChoice.Value = AutomaticDiagnosticKey;
                _config.Save();
            }
            catch
            {
                AutomaticDiagnosticConsent.Value = "";
                AutomaticDiagnosticChoice.Value = previousChoice;
                throw;
            }
        }

        public void SetDiagnosticConsent(bool allowed)
        {
            DiagnosticConsent.Value = allowed ? DiagnosticUploadClient.ConsentKey(new Uri(DiagnosticUploadClient.DefaultEndpoint)) : "";
            DiagnosticChoiceMade.Value = true;
            _config.Save();
        }

        public void SavePanelMargin(float x, float y)
        {
            MarginX.Value = x;
            MarginY.Value = y;
            _config.Save();
        }

        private static OptionRow Switch(string label, ConfigEntry<bool> entry) => new OptionRow
        {
            Label = label,
            Choices = new[] { "꺼짐", "켜짐" },
            Read = () => entry.Value ? 1 : 0,
            Write = i => entry.Value = i == 1,
        };

        private static OptionRow Steps(
            string label, ConfigEntry<float> entry, float[] steps, string[] names) => new OptionRow
            {
                Label = label,
                Choices = names,
                Read = () => Nearest(steps, entry.Value),
                Write = i => entry.Value = steps[i],
            };

        private OptionRow Key(string label, ConfigEntry<KeyboardShortcut> entry) => new OptionRow
        {
            Label = label,
            ShortcutEntry = entry,
            Shortcut = () => entry.Value,
            Bind = value => entry.Value = value,
            Reset = () => entry.Value = (KeyboardShortcut)entry.DefaultValue,
            Warning = () => ShortcutWarning(entry),
        };

        private string ShortcutWarning(ConfigEntry<KeyboardShortcut> entry)
        {
            var value = entry.Value;
            if (value.MainKey == KeyCode.None)
                return entry == SettingsKey
                    ? "설정 창 단축키가 해제됐습니다. 닫기 전에 다시 지정하거나 BepInEx 설정 파일에서 복원하세요."
                    : "";
            var warnings = new List<string>();
            if (GameKeys.Contains(value.MainKey) || value.Modifiers.Any(GameKeys.Contains))
                warnings.Add("게임 기본 조작과 겹칠 수 있습니다. 조합키도 게임 입력을 막지 않습니다.");
            var duplicates = Rows().Where(row => row.Shortcut != null &&
                row.ShortcutEntry != entry && row.Shortcut().Equals(value))
                .Select(row => row.Label).ToArray();
            if (duplicates.Length > 0)
                warnings.Add("같은 단축키: " + string.Join(", ", duplicates) + ". 함께 실행될 수 있습니다.");
            return string.Join("\n", warnings);
        }

        private static int Nearest(float[] steps, float value)
        {
            var best = 0;
            for (var i = 1; i < steps.Length; i++)
            {
                if (Mathf.Abs(steps[i] - value) < Mathf.Abs(steps[best] - value)) best = i;
            }
            return best;
        }

        /// <summary>
        /// 기본값을 옮겨도 설정 파일에 남은 옛 값이 그대로 쓰인다. 그 값이 게임 키와 겹쳐서 옮긴
        /// 것이므로 남아 있으면 옮긴 뜻이 없다. 정확히 그 값일 때만 새 기본값으로 되돌린다 -
        /// 사용자가 손으로 정한 것은 건드리지 않는다.
        /// </summary>
        private void Retire(ConfigEntry<KeyboardShortcut> entry, KeyboardShortcut retired)
        {
            if (entry.Value.ToString() != retired.ToString()) return;

            _shortcutRevision++;
            entry.Value = (KeyboardShortcut)entry.DefaultValue;
            _log($"{entry.Definition.Key} 가 게임 키와 겹쳐 {entry.Value} 로 옮겼습니다.");
        }

        private void WarnIfGameKey(ConfigEntry<KeyboardShortcut> entry)
        {
            if (!GameKeys.Contains(entry.Value.MainKey)) return;

            _log($"{entry.Definition.Key} 의 {entry.Value.MainKey} 키는 게임도 쓰는 키라 게임 조작이 " +
                 "함께 발동합니다. F 키는 게임이 쓰지 않습니다.");
        }

        /// <summary>
        /// 게임이 실제로 읽는 키. `sharedassets0.assets` 의 InputActionAsset 바인딩에서 뽑았다
        /// (docs/RESEARCH.md 의 "게임 단축키"). 게임은 수정키를 보지 않으므로 Ctrl·Alt 를 붙여도
        /// 여기 걸린 키는 게임 조작을 함께 발동시킨다. F 키는 하나도 쓰지 않는다.
        /// </summary>
        private static readonly HashSet<KeyCode> GameKeys = new HashSet<KeyCode>
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8,
            KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E, KeyCode.F, KeyCode.G,
            KeyCode.P, KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.V, KeyCode.W, KeyCode.X, KeyCode.Z,
            KeyCode.BackQuote, KeyCode.Slash, KeyCode.Space, KeyCode.Tab,
            KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape,
            KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
            KeyCode.LeftControl, KeyCode.LeftShift,
        };

        public static string Describe(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return "미지정";
            var text = "";
            foreach (var modifier in shortcut.Modifiers) text += Short(modifier) + "+";
            return text + Short(shortcut.MainKey);
        }

        private static string Short(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl: return "왼Ctrl";
                case KeyCode.RightControl: return "오른Ctrl";
                case KeyCode.LeftAlt: return "왼Alt";
                case KeyCode.RightAlt: return "오른Alt";
                case KeyCode.LeftShift: return "왼Shift";
                case KeyCode.RightShift: return "오른Shift";
                case KeyCode.Return: return "Enter";
                default: return key.ToString();
            }
        }
    }
}
