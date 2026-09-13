using System;
using SephPlanner.Core.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 새 버전을 알리고, 받을지 묻고, 결과를 보여 준다. 창 하나가 네 단계를 돈다.
    ///
    /// 받는 동안에는 닫아도 된다 - 내려받기는 백그라운드에서 이어지고 끝나면 결과 단계로
    /// 다시 뜬다. 게임을 붙잡아 두면서까지 보여 줄 것이 없다.
    /// </summary>
    internal sealed class UpdateWindow : PlannerWindow
    {
        public enum Stage { Offer, Working, Done, Failed }

        private readonly Action<Version> _accept;
        private readonly Action _later;
        private TextMeshProUGUI _body;
        private LayoutElement _bodySize;
        private RectTransform _offerRow;
        private RectTransform _closeRow;
        private TextMeshProUGUI _acceptButton;
        private TextMeshProUGUI _closeButton;
        private Stage _stage;
        private Version _latest = new Version(0, 0, 0);
        private Version _current = new Version(0, 0, 0);
        private string _message = "";
        private bool _answered;

        public UpdateWindow(Action<Version> accept, Action later)
        {
            _accept = accept;
            _later = later;
        }

        protected override string Title => "SephPlanner 업데이트";
        protected override float WidthRatio => 34f;
        protected override GameObject DefaultFocus =>
            _stage == Stage.Offer ? _acceptButton?.gameObject : _closeButton?.gameObject;

        public Stage Current => _stage;

        /// <summary>어느 단계를 보일지 정한다. 열려 있으면 그 자리에서 바뀌고, 아니면 다음 열기에 반영된다.</summary>
        public void Show(Stage stage, Version latest, Version current, string message = "")
        {
            _stage = stage;
            _latest = latest;
            _current = current;
            _message = message;
            _answered = stage != Stage.Offer;
            Refresh();
        }

        protected override void BuildBody(RectTransform content)
        {
            _body = Widgets.Paragraph("Body", content, Skin, S(0.85f), NativeSkin.Text);
            _bodySize = Widgets.Fixed(_body.rectTransform, S(1f));

            _offerRow = Widgets.Rect("Offer", content);
            Widgets.Row(_offerRow, S(0.5f));
            _acceptButton = Widgets.Clickable("Accept", _offerRow, Skin, S(0.85f), NativeSkin.Mint, Accept);
            _acceptButton.text = "업데이트";
            Widgets.Fixed(_acceptButton.rectTransform, S(1.8f), S(13f));
            var later = Widgets.Clickable("Later", _offerRow, Skin, S(0.85f), NativeSkin.Text, Later);
            later.text = "나중에";
            Widgets.Fixed(later.rectTransform, S(1.8f), S(13f));

            _closeRow = Widgets.Rect("Close", content);
            Widgets.Row(_closeRow, S(0.5f));
            _closeButton = Widgets.Clickable("Confirm", _closeRow, Skin, S(0.85f), NativeSkin.Mint, Close);
            _closeButton.text = "확인";
            Widgets.Fixed(_closeButton.rectTransform, S(1.8f), S(13f));
        }

        public override void Refresh()
        {
            base.Refresh();
            if (_body == null) return;

            var latest = UpdateClient.Format(_latest);
            switch (_stage)
            {
                case Stage.Offer:
                    _body.text = "새 버전 " + latest + " 이 있습니다. 지금 버전은 " + UpdateClient.Format(_current) + " 입니다.\n\n" +
                        "업데이트를 누르면 GitHub Releases 에서 받아 BepInEx/plugins 의 SephPlanner DLL 두 개를 바꿉니다. " +
                        "BepInEx 와 설정, 빌드 지정은 그대로 둡니다.\n\n" +
                        "새 버전은 게임을 다시 시작한 뒤부터 적용됩니다. 무엇이 바뀌었는지는 " +
                        "github.com/nyabi-gh/SephPlanner/releases 에 있습니다.";
                    SetHint("ESC: 나중에");
                    break;
                case Stage.Working:
                    _body.text = latest + " 을 받는 중입니다. 게임을 계속할 수 있고, 끝나면 다시 알립니다.";
                    SetHint("ESC 로 닫아도 계속 받습니다");
                    break;
                case Stage.Done:
                    _body.text = latest + " 을 설치했습니다.\n\n" +
                        "게임을 다시 시작하면 새 버전이 적용됩니다. 그때까지는 지금 버전이 계속 돕니다.";
                    SetHint("ESC 로 닫기");
                    break;
                default:
                    _body.text = "업데이트하지 못했습니다.\n\n" + _message + "\n\n" +
                        "지금 버전은 그대로입니다. github.com/nyabi-gh/SephPlanner/releases 에서 직접 받을 수 있습니다.";
                    SetHint("ESC 로 닫기");
                    break;
            }
            Widgets.FitHeight(_body, _bodySize, S(WidthRatio - 2f));
            Widgets.SetActive(_offerRow, _stage == Stage.Offer);
            Widgets.SetActive(_closeRow, _stage == Stage.Done || _stage == Stage.Failed);
        }

        private void Accept()
        {
            if (_answered) return;
            _answered = true;
            _accept(_latest);
        }

        private void Later()
        {
            if (_answered) return;
            _answered = true;
            Close();
            _later();
        }

        /// <summary>물어보는 단계에서 ESC 나 다른 창에 밀려 닫힌 것은 "나중에" 다.</summary>
        protected override void Closed()
        {
            base.Closed();
            if (_answered) return;
            _answered = true;
            _later();
        }

        protected override void Cleared()
        {
            _body = null;
            _bodySize = null;
            _offerRow = null;
            _closeRow = null;
            _acceptButton = null;
            _closeButton = null;
        }
    }
}
