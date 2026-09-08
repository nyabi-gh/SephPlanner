using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 설정 한 줄. 고를 것이 정해진 값 하나이고, 그 값이 무슨 뜻인지는 창이 알 필요가 없다.
    /// </summary>
    internal sealed class OptionRow
    {
        public string Label;
        public string[] Choices;
        public Func<int> Read;
        public Action<int> Write;

        public ConfigEntry<KeyboardShortcut> ShortcutEntry;
        public Func<KeyboardShortcut> Shortcut;
        public Action<KeyboardShortcut> Bind;
        public Action Reset;
        public Func<string> Warning;
    }

    /// <summary>표시 설정과 단축키를 고르는 창. 단축키로 열고 닫는다.</summary>
    internal sealed class SettingsWindow : PlannerWindow
    {
        private readonly Func<List<OptionRow>> _source;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<(OptionRow Option, TextMeshProUGUI Value)> _keys =
            new List<(OptionRow, TextMeshProUGUI)>();
        private readonly ShortcutCapture<KeyCode> _capture =
            new ShortcutCapture<KeyCode>(IsModifier, KeyCode.Escape);
        private readonly List<KeyCode> _held = new List<KeyCode>();
        private readonly List<KeyCode> _pressed = new List<KeyCode>();
        private KeyCode[] _supported;
        private OptionRow _selected;
        private CanvasGroup _controls;
        private GameObject _tabFocus;
        private GameObject _general;
        private GameObject _shortcuts;
        private TextMeshProUGUI _note;
        private LayoutElement _noteSize;
        private TextMeshProUGUI _cancel;
        private bool _showKeys;
        private string _result = "";

        public SettingsWindow(Func<List<OptionRow>> source) => _source = source;

        protected override string Title => "SephPlanner 설정";
        protected override float WidthRatio => 38f;
        protected override GameObject DefaultFocus => _tabFocus;

        protected override void BuildBody(RectTransform content)
        {
            var controls = Widgets.Rect("Controls", content);
            Widgets.Column(controls, S(0.25f));
            _controls = controls.gameObject.AddComponent<CanvasGroup>();
            var tabs = Widgets.Rect("Tabs", controls);
            Widgets.Row(tabs, S(1f));
            _tabFocus = Button(tabs, "표시 설정", 9f, () => ShowKeys(false)).gameObject;
            Button(tabs, "단축키", 9f, () => ShowKeys(true));

            var general = Widgets.Rect("General", controls);
            Widgets.Column(general, S(0.25f));
            _general = general.gameObject;
            var shortcuts = Widgets.Rect("Shortcuts", controls);
            Widgets.Column(shortcuts, S(0.25f));
            _shortcuts = shortcuts.gameObject;
            foreach (var option in _source())
            {
                if (option.Shortcut == null)
                {
                    _rows.Add(new Row(general, Skin, Base, option, Refresh));
                    continue;
                }
                var line = Widgets.Rect("Key_" + option.Label, shortcuts);
                Widgets.Row(line, S(0.3f));
                var label = Widgets.Label("Label", line, Skin, S(0.85f), NativeSkin.Text);
                label.text = option.Label;
                Widgets.Fixed(label.rectTransform, S(1.5f)).flexibleWidth = 1;
                var value = Button(line, "", 15f, () => BeginCapture(option));
                value.fontSize = S(0.8f);
                Button(line, "해제", 3f, () => Change(option, () => option.Bind(KeyboardShortcut.Empty)));
                Button(line, "기본값", 4f, () => Change(option, option.Reset));
                _keys.Add((option, value));
            }
            _note = Widgets.Paragraph("ShortcutNote", content, Skin, S(0.75f), NativeSkin.TextDim);
            _noteSize = Widgets.Fixed(_note.rectTransform, S(1.1f));
            var cancelRow = Widgets.Rect("CancelRow", content);
            Widgets.Row(cancelRow, 0f);
            _cancel = Button(cancelRow, "지정 취소", 8f, CancelCapture);
            var cancelButton = _cancel.GetComponent<Button>();
            cancelButton.navigation = new Navigation { mode = Navigation.Mode.None };
            ShowKeys(_showKeys);
        }

        private TextMeshProUGUI Button(RectTransform parent, string text, float width, Action click)
        {
            var button = Widgets.Clickable("Button", parent, Skin, S(0.9f), NativeSkin.TextBright, () => click());
            button.text = text;
            button.alignment = TextAlignmentOptions.Center;
            Widgets.Fixed(button.rectTransform, S(1.5f), S(width));
            return button;
        }

        private void ShowKeys(bool show)
        {
            _showKeys = show;
            _general.SetActive(!show);
            _shortcuts.SetActive(show);
            Refresh();
        }

        private void BeginCapture(OptionRow option)
        {
            _selected = option;
            _result = "";
            _capture.Begin(Time.frameCount);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Refresh();
        }

        private void Change(OptionRow option, Action change)
        {
            _selected = option;
            try
            {
                change();
                _result = option.Label + ": " + PluginSettings.Describe(option.Shortcut());
            }
            catch (Exception ex)
            {
                _result = "단축키 저장 실패: " + ex.Message;
                Debug.LogError(_result);
            }
            Refresh();
        }

        private void CancelCapture()
        {
            _capture.Cancel();
            _result = "지정을 취소했습니다.";
            Refresh();
        }

        protected override bool HandleEscape()
        {
            if (!_capture.BlocksShortcuts) return false;
            CancelCapture();
            return true;
        }

        protected override void Closed() => _capture.Cancel();

        protected override void Cleared()
        {
            _capture.Cancel();
            _rows.Clear();
            _keys.Clear();
            _selected = null;
        }

        public bool PollShortcutCapture()
        {
            if (!_capture.BlocksShortcuts) return false;
            if (!HasControl) _capture.Cancel();
            _supported ??= UnityInput.Current.SupportedKeyCodes
                .Where(key => key > KeyCode.None && key < KeyCode.Mouse0).Distinct().ToArray();
            _held.Clear();
            _pressed.Clear();
            foreach (var key in _supported)
            {
                if (UnityInput.Current.GetKey(key)) _held.Add(key);
                if (UnityInput.Current.GetKeyDown(key)) _pressed.Add(key);
            }
            var capturing = _capture.Capturing;
            var wasBlocked = _capture.BlocksShortcuts;
            var blocked = _capture.Update(Time.frameCount, _held, _pressed, out var captured);
            if (captured.HasValue)
            {
                var shortcut = new KeyboardShortcut(captured.Value, _held.Where(IsModifier).ToArray());
                Change(_selected, () => _selected.Bind(shortcut));
            }
            else if (capturing && !_capture.Capturing)
                _result = "지정을 취소했습니다.";
            if (IsOpen && (capturing != _capture.Capturing || wasBlocked != _capture.BlocksShortcuts)) Refresh();
            return blocked;
        }

        private static bool IsModifier(KeyCode key) =>
            key == KeyCode.LeftControl || key == KeyCode.RightControl ||
            key == KeyCode.LeftAlt || key == KeyCode.RightAlt ||
            key == KeyCode.LeftShift || key == KeyCode.RightShift ||
            key == KeyCode.LeftCommand || key == KeyCode.RightCommand ||
            key == KeyCode.LeftWindows || key == KeyCode.RightWindows || key == KeyCode.AltGr;

        public override void Refresh()
        {
            foreach (var row in _rows) row.Refresh();
            foreach (var row in _keys)
            {
                var warning = row.Option.Warning();
                row.Value.text = _capture.Capturing && row.Option == _selected
                    ? "키 입력 대기…"
                    : (warning.Length > 0 ? "! " : "") + PluginSettings.Describe(row.Option.Shortcut());
            }
            if (_controls != null) _controls.interactable = !_capture.BlocksShortcuts;
            if (_cancel != null) _cancel.transform.parent.gameObject.SetActive(_capture.Capturing);
            if (_note == null) return;
            SetHint("ESC로 닫기 · 키 지정 중에는 ESC로 취소");
            _note.text = !_showKeys ? "단축키는 위의 단축키 탭에서 변경할 수 있습니다."
                : _capture.Capturing ? "누른 키를 먼저 놓고 원하는 키를 누르세요. Ctrl·Alt·Shift 조합 가능(좌우 구분). ESC는 취소입니다."
                : (_result.Length > 0 ? _result + "\n" : "") +
                  (_selected != null && _selected.Warning().Length > 0 ? _selected.Warning() + "\n" : "") +
                  "키 이름을 눌러 지정합니다. !는 충돌 가능성 또는 미지정 안내입니다. 다른 모드의 키는 확인하지 않습니다.";
            Widgets.FitHeight(_note, _noteSize, S(WidthRatio - 1.9f));
        }

        /// <summary>한 줄. 이름표와 좌우 화살표, 그리고 지금 고른 값.</summary>
        private sealed class Row
        {
            private readonly OptionRow _row;
            private readonly TextMeshProUGUI _value;

            /// <summary>이 줄의 첫 화살표. 창을 열 때 컨트롤러 초점을 여기에 준다.</summary>
            public GameObject First { get; private set; }

            public Row(
                RectTransform parent, NativeSkin skin, float b, OptionRow row, Action changed)
            {
                _row = row;

                var line = Widgets.Rect("Row_" + row.Label, parent);
                Widgets.Row(line, b * 0.3f);
                Widgets.Fixed(line, b * 1.5f);

                var label = Widgets.Label("Label", line, skin, b * 0.95f, NativeSkin.Text);
                label.text = row.Label;
                Widgets.Fixed(label.rectTransform, b * 1.5f).flexibleWidth = 1;

                First = Arrow(line, skin, b, "<", -1, changed);

                _value = Widgets.Label(
                    "Value", line, skin, b * 0.95f, NativeSkin.TextBright, TextAlignmentOptions.Center);
                Widgets.Fixed(_value.rectTransform, b * 1.5f, b * 7f);

                Arrow(line, skin, b, ">", 1, changed);
            }

            private GameObject Arrow(
                RectTransform line, NativeSkin skin, float b, string glyph, int delta, Action changed)
            {
                var arrow = Widgets.Clickable(
                    "Arrow", line, skin, b * 1.1f, NativeSkin.TextBright,
                    () =>
                    {
                        Step(delta);
                        changed();
                    });
                arrow.text = glyph;
                arrow.alignment = TextAlignmentOptions.Center;
                Widgets.Fixed(arrow.rectTransform, b * 1.5f, b * 1.6f);
                return arrow.gameObject;
            }

            /// <summary>
            /// 양끝에서 멈춘다. 돌아가게 두었더니 불투명도 100%에서 왼쪽을 누르는 순간 55%로
            /// 떨어졌다 - 순서가 있는 값에서는 끝에서 반대쪽으로 튀는 것이 고장으로 읽힌다.
            /// </summary>
            private void Step(int delta)
            {
                var index = Mathf.Clamp(_row.Read() + delta, 0, _row.Choices.Length - 1);
                _row.Write(index);
            }

            public void Refresh()
            {
                var index = Mathf.Clamp(_row.Read(), 0, _row.Choices.Length - 1);
                _value.text = _row.Choices[index];
            }
        }
    }
}
