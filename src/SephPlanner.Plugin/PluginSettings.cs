using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using SephPlanner.Plugin.Ui;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 플러그인 설정 전부. BepInEx 설정 파일이 원본이고, 게임 설정 창의 우리 탭
    /// (<see cref="OptionsTab"/>)이 <see cref="Rows"/>로 같은 값을 읽고 쓴다. 두 길이 같은
    /// ConfigEntry 를 보므로 어느 쪽으로 고쳐도 어긋나지 않는다.
    /// </summary>
    internal sealed class PluginSettings
    {
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

        public ConfigEntry<KeyboardShortcut> ExpandKey { get; }
        public ConfigEntry<KeyboardShortcut> AutoPlaceKey { get; }
        public ConfigEntry<KeyboardShortcut> OpacityKey { get; }
        public ConfigEntry<KeyboardShortcut> MoveKey { get; }
        public ConfigEntry<KeyboardShortcut> HideKey { get; }

        private readonly Action<string> _log;
        private readonly List<ConfigEntry<KeyboardShortcut>> _shortcuts =
            new List<ConfigEntry<KeyboardShortcut>>();

        public static readonly float[] OpacitySteps = { 1.0f, 0.85f, 0.7f, 0.55f };
        private static readonly float[] ScaleSteps = { 0.8f, 0.9f, 1.0f, 1.15f, 1.3f, 1.5f };
        private static readonly float[] WidthSteps = { 18f, 22f, 26f, 30f, 36f };

        public PluginSettings(ConfigFile config, Action<string> log)
        {
            _log = log;

            PollInterval = config.Bind(
                "General", "PollIntervalSeconds", 0.25f,
                "인벤토리를 다시 읽는 주기(초).");
            DumpKey = config.Bind(
                "General", "DumpCatalogKey", new KeyboardShortcut(KeyCode.F9),
                "석판/아티팩트 데이터를 다시 덤프하고 질의 파서를 검증하는 단축키.");
            InventoryDumpKey = config.Bind(
                "General", "DumpInventoryKey", new KeyboardShortcut(KeyCode.F10),
                "인벤토리 내용을 그대로 파일로 남기는 단축키. 인식 문제를 확인할 때 쓴다.");
            OfferRadius = config.Bind(
                "General", "OfferRadius", 12f,
                "선택지로 볼 상자/상점까지의 거리. 넓히면 멀리 있는 것까지 추천에 들어온다.");

            Panel = config.Bind(
                "NativePanel", "Enabled", true,
                "게임 HUD 안에 점수 패널을 직접 그린다. 별도 오버레이 창과 함께 써도 된다.");
            // 열쇠 이름이 예전과 다르다. 뜻이 바뀌었는데 이름을 그대로 두면 저장된 옛 값이
            // 쓰여 화면이 엉뚱한 곳으로 간다.
            Corner = config.Bind(
                "NativePanel", "Corner", PanelCorner.TopRight,
                "화면을 붙일 모서리. 게임 HUD 와 겹치면 옮긴다.");
            MarginX = config.Bind(
                "NativePanel", "MarginX", 1.5f,
                "모서리에서 가로로 띄울 거리. 게임 HUD 글자 크기의 배수라 해상도가 달라도 같게 보인다.");
            MarginY = config.Bind(
                "NativePanel", "MarginY", 1.5f,
                "모서리에서 세로로 띄울 거리. 이동 모드로 옮기면 여기에 저장된다.");
            Width = config.Bind(
                "NativePanel", "WidthScale", 26f,
                "화면의 가로 폭. 역시 게임 HUD 글자 크기의 배수다. 글씨가 잘리면 키운다.");
            Scale = config.Bind(
                "NativePanel", "Scale", 1.0f,
                "화면 전체의 크기 배율. 1 이 게임 HUD 글자와 같은 크기다.");
            Opacity = config.Bind(
                "NativePanel", "Opacity", 1.0f,
                "화면의 불투명도(0~1).");
            Recommendations = config.Bind(
                "NativePanel", "Recommendations", true,
                "무엇을 집을지에 대한 후보 추천을 계산할지. 끄면 가리는 것이 아니라 계산 자체를 " +
                "건너뛴다. 점수와 배치 제안은 그대로 남는다.");

            // 게임이 쓰지 않는 키로 고른다. 게임은 수정키를 보지 않으므로 Ctrl+Alt 를 붙여도
            // 글자 키는 게임 조작을 함께 발동시킨다(docs/RESEARCH.md 의 "게임 단축키").
            // F 키는 게임이 하나도 쓰지 않으며 F9/F10 이 이미 같은 이유로 쓰이고 있다.
            ExpandKey = config.Bind(
                "NativePanel", "ExpandKey", new KeyboardShortcut(KeyCode.F7),
                "패널을 접고 펴는 단축키. 상자·상점을 열면 저절로 펼쳐진다.");
            AutoPlaceKey = config.Bind(
                "NativePanel", "AutoPlaceKey", new KeyboardShortcut(KeyCode.F8),
                "제안된 배치를 게임에 적용하는 단축키. 싱글플레이에서만 동작한다.");
            OpacityKey = config.Bind(
                "NativePanel", "OpacityKey", new KeyboardShortcut(KeyCode.F5),
                "불투명도를 차례로 바꾸는 단축키.");
            MoveKey = config.Bind(
                "NativePanel", "MoveKey", new KeyboardShortcut(KeyCode.F6),
                "이동 모드. 한 번 누르면 화면이 커서를 따라오고, 다시 누르면 그 자리에 고정된다.");
            HideKey = config.Bind(
                "NativePanel", "HideKey", new KeyboardShortcut(KeyCode.F4),
                "화면을 통째로 숨겼다가 다시 보여준다. 숨어 있어도 계산은 계속 돌아서 다시 " +
                "켜면 곧바로 최신 배치가 뜬다.");

            _shortcuts.Add(ExpandKey);
            _shortcuts.Add(AutoPlaceKey);
            _shortcuts.Add(OpacityKey);
            _shortcuts.Add(MoveKey);
            _shortcuts.Add(HideKey);
            _shortcuts.Add(DumpKey);
            _shortcuts.Add(InventoryDumpKey);

            Retire(ExpandKey, new KeyboardShortcut(KeyCode.P, KeyCode.LeftControl, KeyCode.LeftAlt));
            Retire(AutoPlaceKey, new KeyboardShortcut(KeyCode.Return, KeyCode.LeftControl, KeyCode.LeftAlt));
            foreach (var shortcut in _shortcuts) WarnIfGameKey(shortcut);
        }

        /// <summary>화면을 지을 때 쓰는 값들. 달라지면 지금 떠 있는 화면을 버리고 다시 짓는다.</summary>
        public string LayoutSignature =>
            $"{Corner.Value}/{MarginX.Value:0.###}/{MarginY.Value:0.###}/" +
            $"{Width.Value:0.###}/{Scale.Value:0.###}";

        public void CycleOpacity()
        {
            var next = (Nearest(OpacitySteps, Opacity.Value) + 1) % OpacitySteps.Length;
            Opacity.Value = OpacitySteps[next];
        }

        /// <summary>
        /// 게임 설정 창의 우리 탭이 그릴 줄들. 값은 여기서 읽고 여기로 쓴다 - 탭은 무엇을 고를
        /// 수 있는지만 알고, 그 값이 무슨 뜻인지는 알지 못한다.
        /// </summary>
        public List<OptionRow> Rows()
        {
            var keys = KeyChoices();
            var keyNames = new string[keys.Count];
            for (var i = 0; i < keys.Count; i++) keyNames[i] = keys[i].ToString();

            return new List<OptionRow>
            {
                Switch("인게임 화면", Panel),
                new OptionRow
                {
                    Label = "모서리",
                    Choices = new[] { "왼쪽 위", "오른쪽 위", "왼쪽 아래", "오른쪽 아래" },
                    Read = () => (int)Corner.Value,
                    Write = i => Corner.Value = (PanelCorner)i,
                },
                Steps("크기", Scale, ScaleSteps, new[] { "80%", "90%", "100%", "115%", "130%", "150%" }),
                Steps("폭", Width, WidthSteps,
                    new[] { "아주 좁게", "좁게", "보통", "넓게", "아주 넓게" }),
                Steps("불투명도", Opacity, OpacitySteps, new[] { "100%", "85%", "70%", "55%" }),
                Switch("후보 추천", Recommendations),
                Key("접기/펼치기 키", ExpandKey, keys, keyNames),
                Key("자동 배치 키", AutoPlaceKey, keys, keyNames),
                Key("불투명도 키", OpacityKey, keys, keyNames),
                Key("이동 키", MoveKey, keys, keyNames),
                Key("숨기기 키", HideKey, keys, keyNames),
            };
        }

        private static OptionRow Switch(string label, ConfigEntry<bool> entry) => new OptionRow
        {
            Label = label,
            Choices = new[] { "끄기", "켜기" },
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

        private OptionRow Key(
            string label, ConfigEntry<KeyboardShortcut> entry, List<KeyCode> keys, string[] names)
            => new OptionRow
            {
                Label = label,
                Choices = names,
                Read = () => Mathf.Max(0, keys.IndexOf(entry.Value.MainKey)),
                Write = i => Rebind(entry, keys[i]),
            };

        /// <summary>
        /// 고를 수 있는 단축키. F 키만 두는 것은 게임이 F 를 하나도 쓰지 않기 때문이다
        /// (docs/RESEARCH.md 의 "게임 단축키"). 덤프 키가 이미 쓰고 있는 것은 빼서, 탭에서
        /// 고르는 것만으로는 겹칠 수 없게 한다.
        /// </summary>
        private List<KeyCode> KeyChoices()
        {
            var taken = new HashSet<KeyCode> { DumpKey.Value.MainKey, InventoryDumpKey.Value.MainKey };
            var keys = new List<KeyCode>();
            for (var key = KeyCode.F1; key <= KeyCode.F12; key++)
            {
                if (!taken.Contains(key)) keys.Add(key);
            }

            // 설정 파일에 F 키가 아닌 것이 들어 있으면 목록에 없어 엉뚱한 값이 골라진 것처럼
            // 보인다. 지금 쓰는 키는 언제나 목록에 있어야 한다.
            foreach (var shortcut in _shortcuts)
            {
                if (shortcut == DumpKey || shortcut == InventoryDumpKey) continue;
                if (!keys.Contains(shortcut.Value.MainKey)) keys.Add(shortcut.Value.MainKey);
            }
            return keys;
        }

        /// <summary>
        /// 이미 다른 기능이 쓰는 키를 고르면 그 기능이 지금 키를 물려받는다. 그냥 두면 한 번
        /// 눌러서 둘이 함께 발동하는데, 화면에는 둘 다 제 키를 가진 것처럼 보여 원인을 알 수 없다.
        /// </summary>
        private void Rebind(ConfigEntry<KeyboardShortcut> entry, KeyCode key)
        {
            var previous = entry.Value.MainKey;
            foreach (var other in _shortcuts)
            {
                if (other == entry || other.Value.MainKey != key) continue;

                other.Value = new KeyboardShortcut(previous);
                _log($"{other.Definition.Key} 가 {key} 를 내주고 {previous} 로 옮겼습니다.");
            }

            entry.Value = new KeyboardShortcut(key);
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
            var text = "";
            foreach (var modifier in shortcut.Modifiers) text += Short(modifier) + "+";
            return text + Short(shortcut.MainKey);
        }

        private static string Short(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return "Alt";
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.Return: return "Enter";
                default: return key.ToString();
            }
        }
    }
}
