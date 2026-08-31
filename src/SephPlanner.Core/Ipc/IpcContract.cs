using System;
using System.IO;

namespace SephPlanner.Core.Ipc
{
    /// <summary>
    /// 플러그인과 오버레이 사이의 통신 규약.
    /// localhost HTTP 대신 명명 파이프를 쓴다. 방화벽 팝업이 없고 포트가 충돌하지 않는다.
    /// 다만 명명 파이프는 같은 데스크톱의 다른 프로세스도 열 수 있으므로 보안 격리 수단은 아니다.
    /// </summary>
    public static class IpcContract
    {
        /// <summary>
        /// 스냅샷·명령의 형태나 의미가 바뀌면 반드시 올린다. 필드가 개명·삭제된 채 기본값으로
        /// 역직렬화되면 <c>IsMultiplayer</c> 같은 안전 잠금이 열린 쪽으로 무너지기 때문에,
        /// 오버레이는 버전이 다른 스냅샷을 버리고 플러그인은 버전이 다른 명령을 거부한다.
        /// v2: 명령 파이프가 응답을 돌려주는 양방향이 되고, 오버레이가 스냅샷 버전을 검사한다.
        /// </summary>
        public const int ProtocolVersion = 2;

        public const string PipeName = "SephPlanner.Snapshot.v1";

        /// <summary>오버레이가 플러그인으로 명령을 보내는 역방향 파이프. 자동 배치가 이 길로 간다.</summary>
        public const string CommandPipeName = "SephPlanner.Command.v1";

        public const string TabletDbFile = "tablets.json";
        public const string CharmDbFile = "charms.json";
        public const string ComboDbFile = "combos.json";
        public const string VerificationReportFile = "query-verification.txt";
        public const string CatalogVersionFile = "catalog-version.txt";
        public const string StatMeasurementFile = "stat-measure.json";

        /// <summary>
        /// 덤프에 담기는 내용이 늘어날 때 올린다. 덤프는 첫 실행에 한 번만 만들어지므로, 이 번호가
        /// 없으면 예전 덤프를 가진 사람은 새 항목이 영영 빈 채로 남는다.
        /// v2: 아티팩트 효과 설명을 함께 담는다.
        /// v3: 조화의 수정 계열의 레벨별 배수와, 콤보 가중치를 재기 위한 능력치 표를 담는다.
        /// </summary>
        public const int CatalogVersion = 3;

        /// <summary>플러그인이 덤프한 데이터와 오버레이가 읽는 데이터의 공용 위치.</summary>
        public static string DataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SephPlanner");
    }
}
