using System;
using TMPro;
using UnityEngine;

namespace SephPlanner.Plugin.Ui
{
    internal sealed class DiagnosticConsentWindow : PlannerWindow
    {
        private readonly Action<bool> _choose;
        private TextMeshProUGUI _local;

        public DiagnosticConsentWindow(Action<bool> choose) { _choose = choose; }
        protected override string Title => "F10 진단 전송";
        protected override float WidthRatio => 34f;
        protected override GameObject DefaultFocus => _local == null ? null : _local.gameObject;

        protected override void BuildBody(RectTransform content)
        {
            var text = Widgets.Paragraph("Disclosure", content, Skin, S(0.85f), NativeSkin.Text);
            text.text = "진단을 개발자가 운영하는 비공개 진단 서버로 보낼까요?\n\n" +
                "게임·플러그인 버전, 현재 가방과 빌드 설정, 마지막 게시 계획의 재현 자료, SephPlanner의 최근 로그를 보냅니다. " +
                "재현 자료에는 게임에서 읽은 아이템·석판 카탈로그가 포함됩니다. 사용자 경로 등은 전송용 복사본에서 가립니다.\n\n" +
                "세이브·게임 DLL·다른 모드의 로그와 설정은 보내지 않습니다. 자료는 관리자만 조회하며 14일이 지난 자료는 자동 삭제합니다.\n\n" +
                "동의하면 이후 F10을 누를 때 자동 전송합니다. 전송은 1분에 한 번으로 제한하며 그 사이에는 로컬에 저장합니다. " +
                "F3의 ‘F10 진단 전송’에서 끌 수 있습니다. " +
                "전송하지 않아도 로컬 진단 저장은 사용할 수 있습니다.";
            Widgets.FitHeight(text, Widgets.Fixed(text.rectTransform, S(1f)), S(WidthRatio - 2f));
            var row = Widgets.Rect("Actions", content);
            Widgets.Row(row, S(0.5f));
            var send = Widgets.Clickable("Consent", row, Skin, S(0.85f), NativeSkin.Mint, () => Select(true));
            send.text = "동의하고 켜기";
            Widgets.Fixed(send.rectTransform, S(1.8f), S(13f));
            _local = Widgets.Clickable("LocalOnly", row, Skin, S(0.85f), NativeSkin.Text, () => Select(false));
            _local.text = "로컬 저장만";
            Widgets.Fixed(_local.rectTransform, S(1.8f), S(13f));
        }

        private void Select(bool allowed)
        {
            Close();
            _choose(allowed);
        }

        protected override void Cleared() { _local = null; }
    }
}
