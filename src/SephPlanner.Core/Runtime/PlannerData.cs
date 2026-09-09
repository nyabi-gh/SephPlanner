using System;
using System.IO;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 플러그인과 진단 도구가 공유하는 로컬 데이터 파일 이름과 버전.
    /// </summary>
    public static class PlannerData
    {
        public const string TabletDbFile = "tablets.json";
        public const string CharmDbFile = "charms.json";
        public const string ComboDbFile = "combos.json";
        public const string VerificationReportFile = "query-verification.txt";
        public const string VerificationStatusFile = "query-verification.json";
        public const string CatalogVersionFile = "catalog-version.txt";
        public const string StatMeasurementFile = "stat-measure.json";
        public const string CatalogGenerationsDirectory = "catalog-generations";
        public const string CatalogManifestFile = "manifest.txt";
        public const string ActiveCatalogFile = "active-catalog.txt";
        public const string CatalogRefreshStateFile = "catalog-refresh-state.txt";

        public static readonly string[] RequiredCatalogFiles =
        {
            TabletDbFile,
            CharmDbFile,
            ComboDbFile,
            StatMeasurementFile,
            VerificationReportFile,
            VerificationStatusFile,
            CatalogVersionFile,
        };

        /// <summary>
        /// 덤프에 담기는 내용이 늘어날 때 올린다. 덤프는 첫 실행에 한 번만 만들어지므로, 이 번호가
        /// 없으면 예전 덤프를 가진 사람은 새 항목이 영영 빈 채로 남는다.
        /// v2: 아티팩트 효과 설명을 함께 담는다.
        /// v3: 조화의 수정 계열의 레벨별 배수와, 콤보 가중치를 재기 위한 능력치 표를 담는다.
        /// v4: 아티팩트가 레벨마다 주는 값어치를 잰 표를 담는다. 없으면 점수가 레어도 어림값으로
        ///     물러서므로, 예전 덤프를 가진 사람에게는 이 항목이 반드시 새로 만들어져야 한다.
        /// v5: 게임이 매겨 둔 원가와 사파이어 해금가를 담는다. 능력치로 잴 수 없는 아티팩트의
        ///     값어치를 가늠할 후보라, 쓸 만한지 재려면 먼저 덤프에 있어야 한다.
        /// v6: 자동 배치가 질의 검증 결과를 확인할 수 있는 상태 파일을 담는다.
        /// v7: 데이터와 검증 결과를 generation 묶음으로 원자적으로 게시한다.
        /// v8: 자리에 따라 하는 일이 달라지는 아티팩트들(북향의 침, 거대한 망원경, 헌신의 휘장,
        ///     캘세더니 열쇠)의 판정에 필요한 항목을 담는다. 없으면 그 넷은 평범한 아티팩트로만
        ///     평가되어 아무 자리에나 놓인다.
        /// v9: 값어치 표의 환산 누락 여부와 미환산 능력치를 담는다.
        /// v10: 가중치가 패널티를 키우지 않도록 능력치 이득과 손해를 따로 담는다.
        /// v11: 방향에 따라 마법을 강화하는 효과의 위치와 레벨별 회복 속도를 담는다.
        /// v12: 게임 공통 콤보 데이터의 특수 발동 단계를 포함한다.
        /// v13: 방향별 마법 지원 종류·비용 표와 실제 공격 가능한 마법의 판정을 담는다.
        /// v14: 수량·행별 능력치와 종이의 일치 조건을 담는다.
        /// v15: 능력치별 원본·환산 근거와 마법 치명타 적용 여부를 담는다.
        /// </summary>
        public const int CatalogVersion = 15;

        /// <summary>플러그인이 생성하고 진단 도구가 읽는 데이터 위치.</summary>
        public static string DataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SephPlanner");

        public static string? ActiveDataFile(string fileName)
        {
            if (!IsCatalogFile(fileName) ||
                !CatalogBundleStore.TryGetActive(DataDirectory, null, null, out var info, out _))
                return null;
            return Path.Combine(info.Directory, fileName);
        }

        public static bool IsCatalogFile(string fileName)
        {
            foreach (var required in RequiredCatalogFiles)
            {
                if (string.Equals(required, fileName, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
