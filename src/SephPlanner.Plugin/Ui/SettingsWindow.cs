using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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

        /// <summary>앞줄과 사이를 띄운다. 표시 설정과 단축키처럼 성격이 갈리는 곳에 쓴다.</summary>
        public bool Divider;
    }

    /// <summary>표시 설정과 단축키를 고르는 창. 단축키로 열고 닫는다.</summary>
    internal sealed class SettingsWindow : PlannerWindow
    {
        private readonly Func<List<OptionRow>> _source;
        private readonly List<Row> _rows = new List<Row>();

        public SettingsWindow(Func<List<OptionRow>> source)
        {
            _source = source;
        }

        protected override string Title => "SephPlanner 설정";
        protected override float WidthRatio => 30f;
        protected override GameObject DefaultFocus => _rows.Count > 0 ? _rows[0].First : null;

        protected override void BuildBody(RectTransform content)
        {
            // 고를 수 있는 것은 판이 바뀌어도 그대로다. 한 번 짓고 값만 다시 읽는다.
            foreach (var row in _source())
            {
                if (row.Divider) Divider(content);
                _rows.Add(new Row(content, Skin, Base, row, Refresh));
            }
            Origin += $" 줄 {_rows.Count}개";
        }

        protected override void Cleared() => _rows.Clear();

        public override void Refresh()
        {
            foreach (var row in _rows) row.Refresh();
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
