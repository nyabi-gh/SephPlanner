using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 커서를 올린 것을 설명하는 쪽지.
    ///
    /// <b>입력을 가져가지 않는다.</b> 오버레이에서는 마우스가 올라온 것을 WPF 가 알려 줬지만,
    /// 게임 안에서 그 길을 쓰려면 그리는 것마다 <c>raycastTarget</c>을 켜야 하고 그 순간 HUD 가
    /// 게임 조작을 가로챈다. 대신 커서 좌표를 읽어 칸의 사각형과 겹치는지 우리가 직접 센다 -
    /// 좌표를 읽기만 하므로 무입력 보장이 그대로다(<see cref="NativeHud.UpdateHover"/>).
    ///
    /// <b>HUD 안이 아니라 캔버스에 직접 붙는다.</b> HUD 는 세로로 쌓는 배치라, 그 안에 넣으면
    /// 쪽지가 목록의 한 줄이 되어 버린다. 자리를 우리가 정해야 하므로 배치 밖에 두어야 한다.
    /// </summary>
    internal sealed class Tooltip
    {
        private Canvas _canvas;
        private RectTransform _canvasRect;
        private GameObject _root;
        private RectTransform _rect;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;
        private float _base;
        private float _textWidth;
        private LayoutElement _titleSize;
        private LayoutElement _bodySize;
        private string _shown = "";

        public bool IsAlive => _root != null;

        public void Create(UIRoot root, NativeSkin skin, float b)
        {
            if (IsAlive) return;

            _base = b;
            _canvas = root.Canvas;
            _canvasRect = (RectTransform)root.transform;

            var frame = Widgets.Fill("SephPlannerTooltip", root.transform, NativeSkin.Frame);
            _root = frame.gameObject;
            _root.AddComponent<SephPlannerWidget>();
            _root.SetActive(false);

            _rect = frame.rectTransform;
            _rect.anchorMin = new Vector2(0.5f, 0.5f);
            _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0f, 1f);
            _rect.sizeDelta = new Vector2(b * 22f, 0f);

            var edge = Mathf.Max(1, Mathf.RoundToInt(b * 0.2f));
            Widgets.Column(_rect, 0f, new RectOffset(edge, edge, edge, edge));

            // 높이는 글이 정한다. 폭만 우리가 잡고 세로는 내용에 맡긴다.
            var fitter = Widgets.Fitter(_rect);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var body = Widgets.Fill("Body", _rect, NativeSkin.PanelFill);
            var pad = Mathf.RoundToInt(b * 0.5f);
            Widgets.Column(body.rectTransform, b * 0.2f, new RectOffset(pad, pad, pad, pad));

            // 글이 실제로 흐를 폭. 테두리와 안쪽 여백을 뺀 값이라야 잰 높이가 맞는다.
            _textWidth = _rect.sizeDelta.x - 2f * edge - 2f * pad;

            _title = Widgets.Paragraph("Title", body.rectTransform, skin, b * 0.95f, NativeSkin.TextBright);
            _titleSize = Widgets.Fixed(_title.rectTransform, b);

            _body = Widgets.Paragraph("Text", body.rectTransform, skin, b * 0.8f, NativeSkin.Text);
            _bodySize = Widgets.Fixed(_body.rectTransform, b);
        }

        /// <summary>
        /// 쪽지를 커서 옆에 띄운다. 같은 글이면 자리만 옮긴다 - 글이 바뀔 때만 크기를 다시 재면
        /// 되고, 그 계산이 이 화면에서 제일 무겁다.
        /// </summary>
        public void Show(string title, string body, Vector2 cursor)
        {
            if (!IsAlive) return;

            var key = title + " | " + body;
            if (!_root.activeSelf)
            {
                _root.SetActive(true);

                // 다른 창이 열려 있으면 그 위에 얹혀야 한다. 형제 순서가 곧 그리는 순서다.
                // 뜰 때 한 번이면 되고, 매 프레임 옮기면 캔버스를 그때마다 다시 짓게 한다.
                _root.transform.SetAsLastSibling();
            }

            if (key != _shown)
            {
                _shown = key;
                _title.text = title;
                _body.text = body;
                Widgets.FitHeight(_title, _titleSize, _textWidth);
                Widgets.FitHeight(_body, _bodySize, _textWidth);
                Widgets.SetActive(_body, body.Length > 0);

                // 자리를 잡으려면 얼마나 큰지 알아야 하는데, 세로 크기는 배치가 한 번 돌아야 나온다.
                LayoutRebuilder.ForceRebuildLayoutImmediate(_rect);
            }

            Place(cursor);
        }

        public void Hide()
        {
            if (IsAlive && _root.activeSelf) _root.SetActive(false);
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);

            _root = null;
            _shown = "";
        }

        /// <summary>
        /// 커서의 오른쪽 아래에 두되, 화면 밖으로 나가면 반대쪽으로 접는다. 게임 화면의 어느
        /// 구석에서 가리키든 글이 잘리지 않아야 한다.
        /// </summary>
        private void Place(Vector2 cursor)
        {
            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, cursor, camera, out var local)) return;

            var area = _canvasRect.rect;
            var size = _rect.rect.size;
            var gap = _base * 0.8f;

            var x = local.x + gap;
            if (x + size.x > area.xMax) x = local.x - gap - size.x;

            var y = local.y - gap;
            if (y - size.y < area.yMin) y = local.y + gap + size.y;

            _rect.anchoredPosition = new Vector2(
                Mathf.Clamp(x, area.xMin, Mathf.Max(area.xMin, area.xMax - size.x)),
                Mathf.Clamp(y, Mathf.Min(area.yMax, area.yMin + size.y), area.yMax));
        }
    }
}
