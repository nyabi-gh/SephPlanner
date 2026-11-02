using static SephPlanner.Core.Text.Localization;

namespace SephPlanner.Plugin
{
    /// <summary>이 판에 네트워크를 쓰는 기능(진단 전송, 게임 안 업데이트)이 들어 있는가.</summary>
    internal static class Network
    {
        public const bool Available = false;
    }

    /// <summary>
    /// 창작마당 판의 진단은 이 PC 에만 둔다. 창작마당 모드는 정보 수집·외부 전송이 금지돼 있고(공식 가이드 규칙 5),
    /// 동의를 받은 전송이 되는지는 개발사에 묻고 답을 기다린다(docs/LEGAL.md 의 "진단 전송 문의").
    /// 업데이트는 Steam 이 한다. 나머지 네트워크 자리는 몸체 없는 partial void 라 빠진다.
    /// </summary>
    internal sealed partial class SephPlannerPlugin
    {
        private partial void Deliver(DiagnosticCapture capture) =>
            ReportDiagnostic(capture.HasFailures
                ? Tr("진단 일부 저장에 실패했습니다. 저장한 자료와 실패 기록은 로컬에 보관했습니다.")
                : Tr("진단을 이 PC에 저장했습니다: {0}", capture.DirectoryPath));
    }
}
