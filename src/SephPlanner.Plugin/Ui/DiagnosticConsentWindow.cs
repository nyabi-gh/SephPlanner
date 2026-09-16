using System;
using TMPro;
using UnityEngine;

namespace SephPlanner.Plugin.Ui
{
    internal sealed class DiagnosticConsentWindow : PlannerWindow
    {
        private readonly Action<bool> _choose;
        private readonly Action _dismissed;
        private TextMeshProUGUI _local;
        private bool _answered;

        public DiagnosticConsentWindow(Action<bool> choose, Action dismissed)
        {
            _choose = choose;
            _dismissed = dismissed;
        }
        protected override string Title => "진단 서버 전송 동의";
        protected override float WidthRatio => 34f;
        protected override GameObject DefaultFocus => _local == null ? null : _local.gameObject;

        protected override void BuildBody(RectTransform content)
        {
            var text = Widgets.Paragraph("Disclosure", content, Skin, S(0.85f), NativeSkin.Text);
            text.text = "진단을 개발자가 운영하는 비공개 진단 서버로 보낼까요?\n\n" +
                "게임·플러그인 버전, 현재 가방과 빌드 설정, 마지막 게시 계획의 재현 자료, SephPlanner의 최근 로그를 보냅니다. " +
                "재현 자료에는 게임에서 읽은 아이템·석판 카탈로그가 포함됩니다. 사용자 경로 등은 전송용 복사본에서 가립니다.\n\n" +
                "세이브·게임 DLL·다른 모드의 로그와 설정은 보내지 않습니다. 자료는 관리자만 조회하며 14일이 지난 자료는 자동 삭제합니다.\n\n" +
                "동의하면 문제 진단(기본 F10)을 저장한 뒤 메모를 적고 전송할 수 있습니다. 메모 창에서 취소하면 이 PC에만 저장합니다. " +
                "전송은 1분에 한 번으로 제한하며 그 사이에는 이 PC에만 저장합니다. " +
                "설정 창(기본 F3)의 ‘진단 서버 전송’에서 끌 수 있습니다. " +
                "전송에 동의하지 않아도 이 PC에 진단 자료를 저장할 수 있습니다.";
            Widgets.FitHeight(text, Widgets.Fixed(text.rectTransform, S(1f)), S(WidthRatio - 2f));
            var row = Widgets.Rect("Actions", content);
            Widgets.Row(row, S(0.5f));
            var send = Widgets.Clickable("Consent", row, Skin, S(0.85f), NativeSkin.Mint, () => Select(true));
            send.text = "서버 전송에 동의";
            Widgets.Fixed(send.rectTransform, S(1.8f), S(13f));
            _local = Widgets.Clickable("LocalOnly", row, Skin, S(0.85f), NativeSkin.Text, () => Select(false));
            _local.text = "이 PC에만 저장";
            Widgets.Fixed(_local.rectTransform, S(1.8f), S(13f));
        }

        private void Select(bool allowed)
        {
            _answered = true;
            Close();
            _choose(allowed);
        }

        /// <summary>
        /// ESC 로 닫은 것은 "이번에는 전송하지 않기"다. 설정은 그대로 두되, 붙잡고 있던 진단은
        /// 여기서 매듭지어야 한다 - 그러지 않으면 수집한 자료가 설명 파일 없이 남는다.
        /// </summary>
        protected override void Closed()
        {
            base.Closed();
            if (_answered) return;
            _dismissed();
        }

        protected override void Cleared() { _local = null; }
    }
}
