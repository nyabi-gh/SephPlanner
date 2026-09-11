using System;
using System.Collections.Generic;
using SephPlanner.Core.Runtime;
using TMPro;
using UnityEngine;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// F10 진단에 붙일 메모를 받는다.
    ///
    /// 진단 자료는 무엇이 일어났는지는 말해 주지만 무엇이 이상했는지는 말해 주지 않는다. 분류
    /// 하나만 눌러도 제보가 갈리고, 한 줄만 적혀 있어도 재현할 판을 훨씬 빨리 찾는다.
    ///
    /// 적지 않고 넘어가는 길을 항상 열어 둔다 - 메모를 받으려고 진단을 못 보내게 만들면 제보
    /// 자체가 줄어든다.
    /// </summary>
    internal sealed class DiagnosticNoteWindow : PlannerWindow
    {
        private readonly Action<DiagnosticNote> _done;
        private readonly List<(DiagnosticCategory Category, TextMeshProUGUI Label)> _buttons =
            new List<(DiagnosticCategory, TextMeshProUGUI)>();
        private TMP_InputField _input;
        private TextMeshProUGUI _count;
        private string _selected = "";
        private bool _sent;

        public DiagnosticNoteWindow(Action<DiagnosticNote> done) { _done = done; }

        protected override string Title => "F10 진단 메모";
        protected override float WidthRatio => 34f;
        protected override GameObject DefaultFocus => _input == null ? null : _input.gameObject;

        /// <summary>창을 열 때마다 지난 메모가 남아 있으면 안 된다.</summary>
        public void Reset()
        {
            _sent = false;
            _selected = "";
            if (_input != null) _input.text = "";
            Refresh();
        }

        protected override void BuildBody(RectTransform content)
        {
            var ask = Widgets.Paragraph("Ask", content, Skin, S(0.85f), NativeSkin.Text);
            ask.text = "무엇이 이상했는지 알려 주시면 훨씬 빨리 찾습니다. 둘 다 건너뛰어도 진단은 그대로 전송됩니다.";
            Widgets.FitHeight(ask, Widgets.Fixed(ask.rectTransform, S(1f)), S(WidthRatio - 2f));

            BuildCategories(content);
            Divider(content);

            _input = Widgets.Input("Note", content, Skin, S(0.85f), DiagnosticNote.MaximumTextLength, S(0.4f));
            Widgets.Fixed(_input.GetComponent<RectTransform>(), S(5.5f));
            ((TextMeshProUGUI)_input.placeholder).text = "예: 자물쇠가 F8 마다 다른 자리로 갑니다 (선택)";
            _input.onValueChanged.AddListener(_ => UpdateCount());
            _input.onSubmit.AddListener(_ => Send());

            _count = Widgets.Label("Count", content, Skin, S(0.7f), NativeSkin.TextDim, TextAlignmentOptions.MidlineRight);
            Widgets.Fixed(_count.rectTransform, S(1f));

            var row = Widgets.Rect("Actions", content);
            Widgets.Row(row, S(0.5f));
            var send = Widgets.Clickable("Send", row, Skin, S(0.85f), NativeSkin.Mint, Send);
            send.text = "보내기 (Enter)";
            Widgets.Fixed(send.rectTransform, S(1.8f), S(13f));
            var skip = Widgets.Clickable("Skip", row, Skin, S(0.85f), NativeSkin.Text, SendWithoutNote);
            skip.text = "메모 없이 보내기";
            Widgets.Fixed(skip.rectTransform, S(1.8f), S(13f));

            UpdateCount();
        }

        private void BuildCategories(RectTransform content)
        {
            _buttons.Clear();
            RectTransform row = null;
            var inRow = 0;
            foreach (var category in DiagnosticNote.Categories)
            {
                if (row == null || inRow == 3)
                {
                    row = Widgets.Rect("Categories", content);
                    Widgets.Row(row, S(0.4f));
                    Widgets.Fixed(row, S(1.6f));
                    inRow = 0;
                }
                var id = category.Id;
                var label = Widgets.Clickable("Category" + id, row, Skin, S(0.8f), NativeSkin.Text, () => Choose(id));
                Widgets.Fixed(label.rectTransform, S(1.5f), S(9f));
                _buttons.Add((category, label));
                inRow++;
            }
        }

        private void Choose(string id)
        {
            _selected = string.Equals(_selected, id, StringComparison.Ordinal) ? "" : id;
            Refresh();
            // 분류를 누르면 커서가 버튼으로 옮겨 간다. 이어서 적을 수 있게 칸으로 돌려준다.
            if (_input != null) _input.ActivateInputField();
        }

        public override void Refresh()
        {
            base.Refresh();
            foreach (var (category, label) in _buttons)
            {
                if (label == null) continue;
                var chosen = string.Equals(_selected, category.Id, StringComparison.Ordinal);
                label.text = (chosen ? "● " : "○ ") + category.Label;
                label.color = chosen ? NativeSkin.Mint : NativeSkin.Text;
            }
        }

        private void UpdateCount()
        {
            if (_count == null || _input == null) return;
            _count.text = _input.text.Length + " / " + DiagnosticNote.MaximumTextLength;
        }

        protected override void ControlEnabled()
        {
            base.ControlEnabled();
            if (_input != null) _input.ActivateInputField();
        }

        /// <summary>ESC 는 메모를 포기하는 것이지 진단을 포기하는 것이 아니다.</summary>
        protected override bool HandleEscape()
        {
            SendWithoutNote();
            return true;
        }

        private void Send() => Finish(DiagnosticNote.Create(_selected, _input == null ? "" : _input.text));

        private void SendWithoutNote() => Finish(DiagnosticNote.None);

        private void Finish(DiagnosticNote note)
        {
            // 버튼과 Enter, ESC 가 겹쳐 들어와도 진단이 두 번 가면 안 된다.
            if (_sent) return;
            _sent = true;
            if (_input != null) _input.DeactivateInputField();
            Close();
            _done(note);
        }

        protected override void Cleared()
        {
            _buttons.Clear();
            _input = null;
            _count = null;
        }
    }
}
