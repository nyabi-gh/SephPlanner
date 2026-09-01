using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임에서 읽은 정적 데이터와 질의 검증 결과를 진단 도구가 읽을 수 있게 저장한다.
    ///
    /// 질의 전수 검증을 한 프레임에 다 하면 게임이 멈추므로 코루틴으로 나눠 실행한다.
    /// </summary>
    internal static class CatalogDump
    {
        private sealed class VerificationStatus
        {
            public string Generation = "";
            public string GameVersion = "";
            public string GameAssemblyId = "";
            public int Comparisons;
            public int Mismatches;
        }

        private static bool _loaded;
        private static bool? _queryVerified;
        private static CatalogRefreshStatus _status;
        private static string _activeGeneration = "";
        private static CatalogBundleWriter _writer;

        public static string BeginRefresh()
        {
            var generation = CatalogBundleStore.NewGeneration();
            _writer = CatalogBundleStore.Begin(
                PlannerData.DataDirectory, generation, Application.version, GameAssemblyId());
            _loaded = true;
            _queryVerified = null;
            _status = CatalogRefreshStatus.Refreshing;
            _activeGeneration = "";
            return generation;
        }

        public static void FailRefresh(string generation, string reason)
        {
            CatalogBundleStore.MarkFailed(PlannerData.DataDirectory, generation, reason);
            _loaded = true;
            _queryVerified = false;
            _status = CatalogRefreshStatus.Failed;
            _activeGeneration = "";
            _writer = null;
        }

        public static IEnumerator WriteRoutine(Action<string> report, string generation)
        {
            if (_writer == null || _writer.Generation != generation)
                throw new InvalidDataException("현재 카탈로그 갱신 generation이 아닙니다.");

            var tablets = ItemCatalog.LoadTablets();
            yield return null;
            var charms = ItemCatalog.LoadCharms();
            yield return null;
            var combos = ItemCatalog.LoadCombos();
            yield return null;
            var measurement = ItemCatalog.LoadStatMeasurement();
            yield return null;

            // 아티팩트가 레벨마다 실제로 주는 값어치를 콤보와 같은 환산율로 재어 정의에 실어 둔다.
            var worth = CharmStatWorth.Apply(charms, measurement);
            yield return null;

            WriteJson(PlannerData.TabletDbFile, tablets);
            WriteJson(PlannerData.CharmDbFile, charms);
            WriteJson(PlannerData.ComboDbFile, combos);
            WriteJson(PlannerData.StatMeasurementFile, measurement);
            yield return null;

            var verification = new QueryVerifier.Report();
            foreach (var _ in QueryVerifier.RunBatched(tablets, verification))
                yield return null;
            WriteText(PlannerData.VerificationReportFile, QueryVerifier.Format(verification));
            WriteJson(PlannerData.VerificationStatusFile, new VerificationStatus
            {
                Generation = generation,
                GameVersion = Application.version,
                GameAssemblyId = GameAssemblyId(),
                Comparisons = verification.Comparisons,
                Mismatches = verification.Mismatches,
            });
            WriteText(PlannerData.CatalogVersionFile, PlannerData.CatalogVersion.ToString());
            _writer.Publish(verification.Comparisons, verification.Mismatches);

            _loaded = true;
            _queryVerified = verification.Passed;
            _status = CatalogRefreshStatus.Ready;
            _activeGeneration = generation;
            _writer = null;

            report($"석판 {tablets.Count}종, 아티팩트 {charms.Count}종, 콤보 {combos.Count}종, " +
                   $"아티팩트 가치 {worth.ByEntity.Count}종 측정. 질의 검증 " +
                   $"{verification.Comparisons}건 중 불일치 {verification.Mismatches}건.");
        }

        /// <summary>
        /// 다시 덤프할 필요가 없는지. 파일이 있는지만 보면 덤프에 항목이 늘어났을 때 예전 덤프를
        /// 가진 사람은 새 항목이 영영 빈 채로 남는다.
        /// </summary>
        public static bool HasCatalog()
        {
            LoadActive();
            return _status == CatalogRefreshStatus.Ready;
        }

        public static bool QueryVerificationPassed()
        {
            LoadActive();
            return _status == CatalogRefreshStatus.Ready && _queryVerified == true;
        }

        public static string ActiveGeneration
        {
            get
            {
                LoadActive();
                return _status == CatalogRefreshStatus.Ready ? _activeGeneration : "";
            }
        }

        private static void LoadActive()
        {
            if (_loaded) return;
            _loaded = true;

            if (!CatalogBundleStore.TryGetActive(
                    PlannerData.DataDirectory, Application.version, GameAssemblyId(),
                    out var info, out _))
            {
                _status = CatalogRefreshStatus.Unavailable;
                _queryVerified = false;
                _activeGeneration = "";
                return;
            }

            _status = CatalogRefreshStatus.Ready;
            _queryVerified = info.VerificationPassed;
            _activeGeneration = info.Generation;
        }

        private static string GameAssemblyId() =>
            typeof(GridInventory).Assembly.ManifestModule.ModuleVersionId.ToString("N");

        private static void WriteJson(string fileName, object value) =>
            WriteText(fileName, JsonConvert.SerializeObject(value, Formatting.Indented));

        private static void WriteText(string fileName, string content) =>
            _writer.WriteText(fileName, content);
    }
}
