using System;
using System.IO;

namespace SephPlanner.Core.Ipc
{
    /// <summary>
    /// 플러그인과 오버레이 사이의 통신 규약.
    /// localhost HTTP 대신 명명 파이프를 쓴다. 방화벽 팝업이 없고 포트가 충돌하지 않으며
    /// 같은 사용자 세션 밖에서는 열 수 없다.
    /// </summary>
    public static class IpcContract
    {
        public const int ProtocolVersion = 1;

        public const string PipeName = "SephPlanner.Snapshot.v1";

        public const string TabletDbFile = "tablets.json";
        public const string CharmDbFile = "charms.json";
        public const string VerificationReportFile = "query-verification.txt";

        /// <summary>플러그인이 덤프한 데이터와 오버레이가 읽는 데이터의 공용 위치.</summary>
        public static string DataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SephPlanner");
    }
}
