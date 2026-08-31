using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SephPlanner.Core.Ipc;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 설정 한 줄. 고를 것이 정해진 값 하나이고, 그 값이 무슨 뜻인지는 <see cref="OptionsTab"/>이
    /// 알 필요가 없다.
    /// </summary>
    internal sealed class OptionRow
    {
        public string Label;
        public string[] Choices;
        public Func<int> Read;
        public Action<int> Write;
    }

    /// <summary>
    /// 게임 설정 창에 우리 탭을 붙인다. HUD 화면(<see cref="NativeHud"/>)은 입력을 하나도
    /// 가져가지 않아 조작이 전부 단축키인데, 켜고 끄기나 크기 같은 것을 단축키와 설정 파일로
    /// 만지는 것은 임시방편이었다. 설정 창은 플레이어가 일부러 연 게임 자신의 창이라 입력을
    /// 받아도 HUD 의 무입력 원칙에 걸리지 않는다.
    ///
    /// <b>만드는 것이 아니라 복제한다.</b> 탭 버튼도 설정 한 줄도 게임 것을 그대로 복제해서
    /// 붙이므로 생김새·마우스 반응·컨트롤러 이동이 저절로 게임과 같아진다. 우리가 하는 일은
    /// 복제본에서 게임 쪽 배선(저장 키에 값을 잇는 바인더와 로컬라이제이션)을 떼고 우리 값을
    /// 물리는 것뿐이다. 붙을 자리는 전부 public 이라 Harmony 패치가 필요 없다.
    /// </summary>
    internal sealed class OptionsTab
    {
        private const string TabName = "플래너";

        private readonly Action<string> _log;
        private UI_OptionsPanel _panel;
        private UI_TabContent _content;
        private RectTransform _buttons;
        private RectTransform _ourButton;
        private RectTransform _sourceButton;
        private RectTransform _notch;
        private float _notchOffset;
        private bool _reported;
        private readonly List<Bound> _rows = new List<Bound>();
        private bool _wasOpen;
        private bool _wasPanelOpen;

        public OptionsTab(Action<string> log)
        {
            _log = log;
        }

        public string Origin { get; private set; } = "";
        public string Blocker { get; private set; } = "";

        public bool IsAttached => _panel != null && _content != null;

        /// <summary>
        /// 아직 못 붙었으면 붙인다. 씬이 바뀌면 설정 창째로 사라지므로 살아 있는지 매번 본다.
        /// </summary>
        public bool TryAttach(IList<OptionRow> rows)
        {
            if (IsAttached) return true;

            var manager = UIManager.Instance;
            if (manager == null)
            {
                Blocker = "UIManager 가 아직 없습니다.";
                return false;
            }

            var panel = manager.GetElement<UI_OptionsPanel>();
            if (panel == null || panel.tab == null)
            {
                Blocker = "게임 설정 창을 찾지 못했습니다.";
                return false;
            }

            var tab = panel.tab;
            if (tab.tabButtons.Length == 0 || tab.tabContents.Length == 0)
            {
                Blocker = "설정 창에 탭이 없습니다.";
                return false;
            }

            var source = FindTemplate(tab);
            if (source.Content < 0)
            {
                Blocker = "본보기로 쓸 설정 줄을 찾지 못했습니다.";
                return false;
            }

            Blocker = "";
            Build(panel, tab, source, rows);
            return true;
        }

        /// <summary>
        /// 탭이 열릴 때 값을 다시 읽는다. 설정 파일을 직접 고쳤거나 단축키로 불투명도를 돌렸을
        /// 수 있어, 열 때마다 맞춰 두지 않으면 화면과 실제 값이 갈린다.
        /// </summary>
        public void Update()
        {
            if (!IsAttached) return;

            // 창이 열려 있어야 레이아웃이 실제 크기를 갖는다. 닫힌 채로 재면 0 이 나온다.
            var panelOpen = _panel.IsOpened;
            if (panelOpen && !_wasPanelOpen)
            {
                FitButtons();

                // 폭을 고친 결과가 좌표에 반영된 뒤라야 노치를 맞출 수 있다.
                Canvas.ForceUpdateCanvases();
                AlignNotch();
                Report();
            }
            _wasPanelOpen = panelOpen;

            var open = _content.IsOpened;
            if (open && !_wasOpen) Refresh();
            _wasOpen = open;
        }

        /// <summary>
        /// 탭이 하나 늘면 버튼 줄이 창 밖으로 밀린다 - 실제로 첫 탭과 우리 탭이 프레임 바깥으로
        /// 잘려 나갔다. 넘칠 때만 버튼 폭을 똑같이 나눠 창 안에 들어오게 한다. 줄이 이미 들어가는
        /// 경우에는 손대지 않으므로, 게임이 탭을 줄이는 패치를 해도 생김새가 그대로 남는다.
        ///
        /// 창을 열 때마다 다시 재는 것은 해상도와 UI 배율이 그 사이 바뀌었을 수 있어서다.
        /// </summary>
        private void FitButtons()
        {
            if (_buttons == null) return;

            var children = new List<RectTransform>();
            for (var i = 0; i < _buttons.childCount; i++)
            {
                var child = _buttons.GetChild(i) as RectTransform;
                if (child != null && child.gameObject.activeSelf) children.Add(child);
            }
            if (children.Count < 2) return;

            var grid = _buttons.GetComponent<GridLayoutGroup>();
            var row = _buttons.GetComponent<HorizontalLayoutGroup>();
            var spacing = grid != null ? grid.spacing.x : row != null ? row.spacing : 0f;
            var padding = grid != null ? grid.padding.horizontal
                : row != null ? row.padding.horizontal : 0;

            var inner = _buttons.rect.width - padding - spacing * (children.Count - 1);
            if (inner <= 0f) return;

            var total = 0f;
            foreach (var child in children) total += child.rect.width;
            if (total <= inner) return;

            var width = inner / children.Count;
            if (grid != null)
            {
                grid.cellSize = new Vector2(width, grid.cellSize.y);
            }
            else
            {
                // 가로 레이아웃이 자식 폭을 제 손으로 정하는지 아닌지에 따라 보는 값이 다르다.
                // 둘 다 맞춰 두면 어느 쪽이든 같은 폭이 나온다.
                foreach (var child in children)
                {
                    var element = child.GetComponent<LayoutElement>();
                    if (element == null) element = child.gameObject.AddComponent<LayoutElement>();

                    element.minWidth = width;
                    element.preferredWidth = width;
                    element.flexibleWidth = 0f;
                    child.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                }
            }

            LayoutRebuilder.MarkLayoutForRebuild(_buttons);
            _log($"탭 버튼 {children.Count}개를 폭 {width:0} 으로 맞췄습니다 " +
                 $"(줄 {inner:0}, 원래 {total:0}, 레이아웃 {(grid != null ? "격자" : row != null ? "가로" : "없음")}).");
        }

        public void Refresh()
        {
            foreach (var row in _rows) row.Refresh();
        }

        private struct Template
        {
            public int Content;
            public int Box;
        }

        /// <summary>
        /// 복제할 탭과 그 안의 본보기 줄. 선택 상자가 가장 많은 탭을 고르는 것은 우리 줄이
        /// 열 개 남짓이라 그만큼 담아 본 적이 있는 그릇이어야 하기 때문이다.
        /// </summary>
        private static Template FindTemplate(UI_Tab tab)
        {
            var found = new Template { Content = -1, Box = 0 };
            var most = 0;

            for (var i = 0; i < tab.tabContents.Length; i++)
            {
                var content = tab.tabContents[i];
                if (content == null) continue;

                var boxes = content.GetComponentsInChildren<UI_HorizontalSelectionBox>(true);
                if (boxes.Length <= most) continue;

                most = boxes.Length;
                found.Content = i;

                // 값을 저장 키에 잇는 바인더가 달린 줄을 고른다. 그 바인더가 어느 글자가 값
                // 표시인지 알려 주므로, 이름표와 값을 짐작으로 가려낼 필요가 없다.
                found.Box = 0;
                for (var j = 0; j < boxes.Length; j++)
                {
                    if (ValueTextOf(RowRoot(boxes[j].transform, content.transform)) == null) continue;

                    found.Box = j;
                    break;
                }
            }
            return found;
        }

        private void Build(UI_OptionsPanel panel, UI_Tab tab, Template source, IList<OptionRow> rows)
        {
            var origin = tab.tabContents[source.Content];
            var clone = UnityEngine.Object.Instantiate(origin.gameObject, origin.transform.parent);
            clone.name = "SephPlannerOptions";

            var content = clone.GetComponent<UI_TabContent>();
            var boxes = clone.GetComponentsInChildren<UI_HorizontalSelectionBox>(true);
            var template = RowRoot(boxes[source.Box].transform, clone.transform);
            var container = template.parent;

            // 본보기를 먼저 복제하고 나서 원래 줄들을 지운다. 순서가 뒤집히면 본보기까지 사라진다.
            var old = new List<GameObject>();
            for (var i = 0; i < container.childCount; i++) old.Add(container.GetChild(i).gameObject);

            _rows.Clear();
            foreach (var row in rows)
            {
                var made = UnityEngine.Object.Instantiate(template.gameObject, container);
                made.name = "Row_" + row.Label;
                made.SetActive(true);

                var bound = Bound.Dress(this, made, row);
                if (bound != null) _rows.Add(bound);
            }

            foreach (var go in old)
            {
                // Destroy 는 프레임 끝에야 실제로 지워진다. 그때까지 레이아웃이 이것들을 세지
                // 않도록 먼저 끈다.
                go.SetActive(false);
                UnityEngine.Object.Destroy(go);
            }

            var button = BuildButton(tab, panel);
            _buttons = button.transform.parent as RectTransform;
            _ourButton = button.transform as RectTransform;
            _sourceButton = tab.tabButtons[source.Content].transform as RectTransform;

            // 선택된 탭에 붙는 흰 탭 모양은 탭 내용 쪽에 있고, 복제본은 원본 탭 자리를 가리킨
            // 채로 온다. 실제로 게임에서 우리 탭을 골랐는데 흰 모양이 첫 탭 위에 얹혀 옆 탭
            // 글자를 반씩 가렸다. 원본 탭에 대해 어긋나 있던 만큼을 기억해 두었다가 우리 탭
            // 자리에 그대로 옮겨 준다.
            _notch = FindNotch(clone.transform);
            if (_notch != null && _sourceButton != null)
                _notchOffset = _notch.position.x - _sourceButton.position.x;

            content.parent = panel;

            // 원래 가리키던 줄은 방금 지웠다. 그대로 두면 탭을 열 때 사라진 것을 고르려 든다.
            content.selectionOnOpened = _rows.Count > 0 ? _rows[0].Selectable : null;
            content.CloseTab();

            tab.tabButtons = Append(tab.tabButtons, button);
            tab.tabContents = Append(tab.tabContents, content);

            _panel = panel;
            _content = content;
            Refresh();

            Origin =
                $"본보기 탭 {source.Content + 1}번(줄 {old.Count}개) -> 우리 줄 {_rows.Count}개" +
                $", 탭 색인 {tab.tabContents.Length - 1}";
        }

        /// <summary>
        /// 흰 탭 모양을 우리 탭 자리로 옮긴다. 창을 열 때마다 절대 위치로 다시 놓으므로 여러 번
        /// 불려도 같은 자리에 선다.
        /// </summary>
        private void AlignNotch()
        {
            if (_notch == null || _ourButton == null) return;

            var position = _notch.position;
            _notch.position = new Vector3(_ourButton.position.x + _notchOffset, position.y, position.z);
        }

        /// <summary>
        /// 탭 내용에서 흰 탭 모양을 찾는다. 내용 판때기보다 위로 삐져나온 것이 그것이다 -
        /// 탭 줄까지 올라가 붙어야 하는 물건이라 혼자만 위쪽으로 넘친다.
        /// </summary>
        private static RectTransform FindNotch(Transform content)
        {
            var root = content as RectTransform;
            if (root == null) return null;

            var corners = new Vector3[4];
            root.GetWorldCorners(corners);
            var top = corners[1].y;

            var width = corners[2].x - corners[0].x;

            RectTransform found = null;
            var highest = top + 1f;

            for (var i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null) continue;

                child.GetWorldCorners(corners);
                if (corners[1].y <= highest) continue;

                // 탭 하나 너비의 돌기여야 한다. 내용 판때기 자체가 제일 위까지 차 있는 구조라면
                // 그것을 옆으로 밀게 되므로, 넓은 것은 후보로 삼지 않는다.
                if (corners[2].x - corners[0].x > width * 0.5f) continue;

                highest = corners[1].y;
                found = child;
            }
            return found;
        }

        /// <summary>
        /// 붙인 결과를 파일로 남긴다. 탭 줄이 어떻게 배치되는지는 게임 데이터만 봐서는 알 수
        /// 없어서, 어긋났을 때 스크린샷을 눈으로 재는 대신 좌표를 읽을 수 있어야 한다.
        /// </summary>
        private void Report()
        {
            if (_reported) return;
            _reported = true;

            var text = new StringBuilder();
            text.AppendLine(Origin);
            text.AppendLine();
            text.AppendLine($"[tab buttons] parent={Describe(_buttons)}");
            for (var i = 0; i < _buttons.childCount; i++)
                text.AppendLine($"  {i}: {Describe(_buttons.GetChild(i) as RectTransform)}");

            text.AppendLine();
            text.AppendLine($"[our content] {Describe(_content.transform as RectTransform)}");
            for (var i = 0; i < _content.transform.childCount; i++)
            {
                var child = _content.transform.GetChild(i) as RectTransform;
                text.AppendLine($"  {(child == _notch ? "*" : " ")}{i}: {Describe(child)}");
            }

            text.AppendLine();
            text.AppendLine($"[notch] {Describe(_notch)} offset={_notchOffset:0.#}");
            text.AppendLine($"[our button] {Describe(_ourButton)}");
            text.AppendLine($"[source button] {Describe(_sourceButton)}");

            try
            {
                Directory.CreateDirectory(IpcContract.DataDirectory);
                var path = Path.Combine(IpcContract.DataDirectory, "options-tab.txt");
                File.WriteAllText(path, text.ToString());
                _log("설정 탭 구조를 남겼습니다 - " + path);
            }
            catch (Exception ex)
            {
                _log("설정 탭 구조를 남기지 못했습니다 - " + ex.Message);
            }
        }

        private static string Describe(RectTransform rect)
        {
            if (rect == null) return "<없음>";

            var components = new StringBuilder();
            foreach (var component in rect.GetComponents<Component>())
            {
                if (component is RectTransform) continue;
                if (components.Length > 0) components.Append(',');
                components.Append(component.GetType().Name);
            }

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return $"{rect.name} x={corners[0].x:0}~{corners[2].x:0} y={corners[0].y:0}~{corners[1].y:0} " +
                   $"active={rect.gameObject.activeSelf} [{components}]";
        }

        /// <summary>
        /// 선택 상자에서 위로 올라가며 "한 줄"의 뿌리를 찾는다. 형제 줄이 함께 들어 있는 곳까지
        /// 올라가면 거기가 줄들의 그릇이므로 그 직전이 한 줄이다. 프리팹이 이름표와 상자를 어떻게
        /// 묶어 두었든 이 기준이면 같은 답이 나온다.
        /// </summary>
        private static Transform RowRoot(Transform box, Transform content)
        {
            var row = box;
            while (row.parent != null && row.parent != content &&
                   row.parent.GetComponentsInChildren<UI_HorizontalSelectionBox>(true).Length <= 1)
            {
                row = row.parent;
            }
            return row;
        }

        /// <summary>
        /// 값이 표시되는 글자. 게임 바인더가 들고 있는 것을 그대로 쓴다 - 없으면 이 줄은 본보기로
        /// 쓰지 않는다.
        /// </summary>
        private static TextMeshProUGUI ValueTextOf(Transform row)
        {
            var integer = row.GetComponentInChildren<UI_OptionBox_Common_Integer>(true);
            if (integer != null && integer.valueText != null) return integer.valueText.text;

            var text = row.GetComponentInChildren<UI_HorizontalSelectionBox_Text>(true);
            return text != null && text.valueText != null ? text.valueText.text : null;
        }

        private static UI_TabButton BuildButton(UI_Tab tab, UI_OptionsPanel panel)
        {
            var index = tab.tabButtons.Length;
            var origin = tab.tabButtons[index - 1];
            var clone = UnityEngine.Object.Instantiate(origin.gameObject, origin.transform.parent);
            clone.name = "SephPlannerTabButton";

            var button = clone.GetComponent<UI_TabButton>();
            Strip(clone);
            var label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = TabName;

            // 게임 탭의 아이콘을 그대로 달고 있으면 남의 탭처럼 보인다. 글자가 있는 자리라야
            // 아이콘을 뗀다 - 둘 다 없으면 빈 버튼이 된다.
            if (label != null) button.SetTabIcon(null);
            button.SetTabButtonSprite(false);

            var clickable = clone.GetComponent<Button>();
            if (clickable != null)
            {
                // 복제본의 onClick 은 원본 탭 색인을 가리킨다. 프리팹에 박힌 것이라 지울 수는
                // 없고 꺼 둔 뒤 새 색인으로 다시 잇는다.
                for (var i = 0; i < clickable.onClick.GetPersistentEventCount(); i++)
                    clickable.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);

                clickable.onClick.RemoveAllListeners();
                clickable.onClick.AddListener(() => panel.SelectTab(index));
            }
            return button;
        }

        private static T[] Append<T>(T[] array, T item)
        {
            var next = new T[array.Length + 1];
            Array.Copy(array, next, array.Length);
            next[array.Length] = item;
            return next;
        }

        /// <summary>
        /// 게임 쪽 배선을 뗀다. 로컬라이제이션은 켜질 때마다 제 열쇠로 글자를 덮어쓰고, 바인더는
        /// 값을 게임 저장 키에 잇는다. 툴팁은 원래 줄의 설명을 그대로 띄워 거짓말이 된다.
        /// <c>enabled</c>를 먼저 끄는 것은 Destroy 가 프레임 끝에야 도는데 그 전에 창이 열리면
        /// OnEnable 이 한 번 도는 것을 막기 위해서다.
        /// </summary>
        private static void Strip(GameObject target)
        {
            foreach (var component in target.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;

                var name = component.GetType().Name;
                if (name != "UI_LocalizationStringText" &&
                    name != "UI_HorizontalSelectionBox_Text" &&
                    !name.StartsWith("UI_OptionBox") &&
                    name.IndexOf("Tooltip", StringComparison.Ordinal) < 0) continue;

                component.enabled = false;
                UnityEngine.Object.Destroy(component);
            }
        }

        /// <summary>복제한 줄 하나와 그것이 읽고 쓰는 값.</summary>
        private sealed class Bound
        {
            private readonly OptionsTab _owner;
            private readonly OptionRow _row;
            private readonly UI_HorizontalSelectionBox _box;
            private readonly TextMeshProUGUI _value;

            private Bound(
                OptionsTab owner, OptionRow row, UI_HorizontalSelectionBox box, TextMeshProUGUI value)
            {
                _owner = owner;
                _row = row;
                _box = box;
                _value = value;
            }

            public GameObject Selectable => _box.gameObject;

            public static Bound Dress(OptionsTab owner, GameObject made, OptionRow row)
            {
                var box = made.GetComponentInChildren<UI_HorizontalSelectionBox>(true);
                if (box == null) return null;

                var value = ValueTextOf(made.transform);
                var texts = made.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (value == null && texts.Length > 0) value = texts[texts.Length - 1];

                TextMeshProUGUI label = null;
                foreach (var text in texts)
                {
                    if (text == value) continue;

                    label = text;
                    break;
                }

                Strip(made);
                if (label != null) label.text = row.Label;

                box.numberOfElements = row.Choices.Length;
                box.overflowType = UI_HorizontalSelectionBox.OverflowType.Clamp;

                // 프리팹에 박힌 콜백이 남아 있으면 우리 값을 바꿀 때 게임 설정도 함께 움직인다.
                // 공개 필드라 통째로 갈아 끼우는 것이 확실하다.
                box.ValueChangedCallback = new UnityEvent<int>();

                var bound = new Bound(owner, row, box, value);
                box.OnValueChanged += bound.Apply;
                bound.Refresh();
                return bound;
            }

            private void Apply(int index)
            {
                if (index < 0 || index >= _row.Choices.Length) return;

                _row.Write(index);

                // 단축키를 맞바꾸는 것처럼 한 줄을 고치면 다른 줄이 따라 바뀌는 경우가 있다.
                // 바뀐 줄만 다시 그리면 화면이 실제 값과 갈린다.
                _owner.Refresh();
            }

            public void Refresh()
            {
                var index = Mathf.Clamp(_row.Read(), 0, _row.Choices.Length - 1);
                _box.ChangeValueWithoutNotify(index);
                if (_value != null) _value.text = _row.Choices[index];
            }
        }
    }
}
